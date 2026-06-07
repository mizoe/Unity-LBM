using UnityEngine;

public class LbmController3D : MonoBehaviour
{
    public enum DisplayMode { Cd_induced, Cp, Cpt, u, v, w }
    [Header("Visualization")]
    public DisplayMode displayMode = DisplayMode.u;
    public float maxVal = 0.2f; // 色の最大値（正規化用）

    [Header("LBM Settings")]
    public int NX = 64;
    public int NY = 64;
    public int NZ = 32;
    [Range(0.51f, 1.5f)] public float tau = 0.52f;
    public float uInlet = 0.1f;

    [Header("Scene References")]
    public GameObject obstacleRoot;
    public Transform emitterSphere; // パーティクルの発生源となるSphere

    [Header("Compute Shaders")]
    public ComputeShader lbmComputeShader;
    public ComputeShader particleComputeShader;

    [Header("Materials")]
    public Material sliceMaterial;
    public Material particleMaterial;

    [Header("Particle Settings")]
    public int numParticles = 10000;
    public float particleSpeed = 10.0f;

    private ComputeBuffer fOldBuffer;
    private ComputeBuffer fNewBuffer;
    private ComputeBuffer obstacleBuffer;
    private ComputeBuffer particleBuffer;
    private RenderTexture velocity3DTexture;

    private int totalCells;
    private Vector3 originPos;
    private Vector3 domainSize = new Vector3(10f, 5f, 3f);

    struct Particle
    {
        public Vector3 pos0;
        public Vector3 pos1;
        public Vector3 pos2;
        public Vector3 pos3;
        public Vector3 pos4;
        public Vector3 velocity;
        public float life;
    }

    void Start()
    {
        totalCells = NX * NY * NZ;
        originPos = transform.position;

        InitializeFluid();
        InitializeObstacles();
        InitializeParticles();
    }

    void InitializeFluid()
    {
        int bufferSize = totalCells * 19;
        float[] initialF = new float[bufferSize];

        float[] w = new float[19] {
            1f/3f, 1f/18f, 1f/18f, 1f/18f, 1f/18f, 1f/18f, 1f/18f,
            1f/36f, 1f/36f, 1f/36f, 1f/36f, 1f/36f, 1f/36f, 1f/36f, 1f/36f, 1f/36f, 1f/36f, 1f/36f, 1f/36f
        };

        int[] ex = { 0, 1, -1, 0, 0, 0, 0, 1, 1, -1, -1, 1, 1, -1, -1, 0, 0, 0, 0 };

        float u_sq = uInlet * uInlet;

        for (int z = 0; z < NZ; z++)
        {
            for (int y = 0; y < NY; y++)
            {
                for (int x = 0; x < NX; x++)
                {
                    for (int q = 0; q < 19; q++)
                    {
                        int idx = q + 19 * (x + NX * (y + NY * z));
                        float edotu = ex[q] * uInlet;
                        initialF[idx] = w[q] * (1.0f + 3.0f * edotu + 4.5f * edotu * edotu - 1.5f * u_sq);
                    }
                }
            }
        }

        fOldBuffer = new ComputeBuffer(bufferSize, sizeof(float));
        fNewBuffer = new ComputeBuffer(bufferSize, sizeof(float));
        fOldBuffer.SetData(initialF);
        fNewBuffer.SetData(initialF);
    }

    void InitializeObstacles()
    {
        int[] obstacleMap = new int[totalCells];
        if (obstacleRoot != null)
        {
            Vector3 cellSize = new Vector3(domainSize.x / NX, domainSize.y / NY, domainSize.z / NZ);
            // ボクセル（セル）がすっぽり収まるくらいの極小の球の半径
            float checkRadius = Mathf.Min(cellSize.x, cellSize.y, cellSize.z) * 0.5f;

            for (int z = 0; z < NZ; z++)
            {
                for (int y = 0; y < NY; y++)
                {
                    for (int x = 0; x < NX; x++)
                    {
                        Vector3 cellPos = originPos + new Vector3((x + 0.5f) * cellSize.x, (y + 0.5f) * cellSize.y, (z + 0.5f) * cellSize.z);
                        int index = x + y * NX + z * NX * NY;
                        obstacleMap[index] = 0;

                        // 球体で空間をサンプリングし、車体内部なら障害物とする
                        Collider[] hitCols = Physics.OverlapSphere(cellPos, checkRadius);
                        foreach (var hit in hitCols)
                        {
                            if (hit.transform.IsChildOf(obstacleRoot.transform))
                            {
                                obstacleMap[index] = 1;
                                break;
                            }
                        }
                    }
                }
            }
        }

        obstacleBuffer = new ComputeBuffer(totalCells, sizeof(int));
        obstacleBuffer.SetData(obstacleMap);
    }

    void InitializeParticles()
    {
        velocity3DTexture = new RenderTexture(NX, NY, 0, RenderTextureFormat.ARGBFloat);
        velocity3DTexture.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
        velocity3DTexture.volumeDepth = NZ;
        velocity3DTexture.enableRandomWrite = true;
        velocity3DTexture.filterMode = FilterMode.Bilinear;
        velocity3DTexture.Create();

        particleBuffer = new ComputeBuffer(numParticles, 76);
        Particle[] pArray = new Particle[numParticles];

        // 実行直後に巨大な箱が発生するのを防ぐため、Sphereの位置を初期位置にする
        Vector3 startGridPos = new Vector3(NX * 0.1f, NY * 0.5f, NZ * 0.5f);
        if (emitterSphere != null)
        {
            Vector3 localPos = emitterSphere.position - originPos;
            startGridPos = new Vector3(
                (localPos.x / domainSize.x) * NX,
                (localPos.y / domainSize.y) * NY,
                (localPos.z / domainSize.z) * NZ
            );
        }

        for (int i = 0; i < numParticles; i++)
        {
            pArray[i].pos0 = startGridPos;
            pArray[i].pos1 = startGridPos;
            pArray[i].pos2 = startGridPos;
            pArray[i].pos3 = startGridPos;
            pArray[i].pos4 = startGridPos;
            pArray[i].velocity = Vector3.zero;
            // 最初からバラけさせるために寿命をランダム化
            pArray[i].life = Random.Range(0.0f, 15.0f);
        }
        particleBuffer.SetData(pArray);
    }

    void Update()
    {
        // ============================================
        // [1] LBMの計算
        // ============================================
        lbmComputeShader.SetInt("NX", NX);
        lbmComputeShader.SetInt("NY", NY);
        lbmComputeShader.SetInt("NZ", NZ);
        lbmComputeShader.SetFloat("tau", tau);
        lbmComputeShader.SetFloat("u_inlet", uInlet);

        int kernelCollide = lbmComputeShader.FindKernel("CollideAndStream");
        lbmComputeShader.SetBuffer(kernelCollide, "f_old", fOldBuffer);
        lbmComputeShader.SetBuffer(kernelCollide, "f_new", fNewBuffer);
        lbmComputeShader.SetBuffer(kernelCollide, "obstacles", obstacleBuffer);
        lbmComputeShader.SetTexture(kernelCollide, "VelocityField", velocity3DTexture);

        int threadsX = Mathf.CeilToInt(NX / 8f);
        int threadsY = Mathf.CeilToInt(NY / 8f);
        int threadsZ = Mathf.CeilToInt(NZ / 8f);
        lbmComputeShader.Dispatch(kernelCollide, threadsX, threadsY, threadsZ);

        ComputeBuffer temp = fOldBuffer;
        fOldBuffer = fNewBuffer;
        fNewBuffer = temp;

        // ============================================
        // [2] パーティクルの計算
        // ============================================
        int kernelUpdate = particleComputeShader.FindKernel("UpdateParticles");
        particleComputeShader.SetBuffer(kernelUpdate, "particles", particleBuffer);
        particleComputeShader.SetTexture(kernelUpdate, "VelocityField", velocity3DTexture);
        particleComputeShader.SetVector("gridSize", new Vector3(NX, NY, NZ));
        particleComputeShader.SetFloat("deltaTime", Time.deltaTime);
        particleComputeShader.SetFloat("particleSpeed", particleSpeed);
        particleComputeShader.SetVector("randomSeed", new Vector2(Random.value, Random.value));

        // Sphereの位置と「3Dセル比率を考慮した半径」を計算して送る
        if (emitterSphere != null)
        {
            Vector3 localPos = emitterSphere.position - originPos;
            Vector3 gridCenter = new Vector3(
                (localPos.x / domainSize.x) * NX,
                (localPos.y / domainSize.y) * NY,
                (localPos.z / domainSize.z) * NZ
            );

            // ワールドのスケールを元に、XYZそれぞれのグリッド数としての半径を算出
            float worldRadius = emitterSphere.localScale.x * 0.5f;
            Vector3 gridRadius3D = new Vector3(
                (worldRadius / domainSize.x) * NX,
                (worldRadius / domainSize.y) * NY,
                (worldRadius / domainSize.z) * NZ
            );

            particleComputeShader.SetVector("emitterCenter", gridCenter);
            particleComputeShader.SetVector("emitterRadius", gridRadius3D);
        }
        else
        {
            particleComputeShader.SetVector("emitterCenter", new Vector3(NX * 0.5f, NY * 0.5f, NZ * 0.5f));
            particleComputeShader.SetVector("emitterRadius", new Vector3(5.0f, 5.0f, 5.0f));
        }

        int pGroups = Mathf.CeilToInt(numParticles / 256f);
        particleComputeShader.Dispatch(kernelUpdate, pGroups, 1, 1);

        // ============================================
        // [3] マテリアルへの転送・描画
        // ============================================
        if (sliceMaterial != null && velocity3DTexture != null)
        {
            sliceMaterial.SetTexture("_VelocityField", velocity3DTexture);
        }

        if (sliceMaterial != null)
        {
            sliceMaterial.SetInt("_Mode", (int)displayMode);
            sliceMaterial.SetFloat("_MaxVal", maxVal);
            sliceMaterial.SetFloat("_Uinlet", uInlet);
            sliceMaterial.SetVector("_DomainOrigin", originPos);
            sliceMaterial.SetVector("_DomainSize", domainSize);
        }

        if (particleMaterial != null)
        {
            particleMaterial.SetBuffer("particles", particleBuffer);
            particleMaterial.SetVector("_GridSize", new Vector3(NX, NY, NZ));
            particleMaterial.SetVector("_DomainSize", domainSize);
            particleMaterial.SetVector("_Origin", originPos);

            Bounds bounds = new Bounds(originPos + domainSize * 0.5f, domainSize);
            Graphics.DrawProcedural(particleMaterial, bounds, MeshTopology.Triangles, 24, numParticles);
        }
    }

    void OnDestroy()
    {
        if (fOldBuffer != null) fOldBuffer.Release();
        if (fNewBuffer != null) fNewBuffer.Release();
        if (obstacleBuffer != null) obstacleBuffer.Release();
        if (particleBuffer != null) particleBuffer.Release();
        if (velocity3DTexture != null) velocity3DTexture.Release();
    }
}