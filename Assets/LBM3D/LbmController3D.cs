using UnityEngine;

public class LbmController3D : MonoBehaviour
{
    // C#側とHLSL側でデータ構造を完全に一致させる (12 + 12 + 4 = 28 bytes)
    struct Particle
    {
        public Vector3 position;
        public Vector3 velocity;
        public float life;
    }

    [Header("Shader References")]
    [SerializeField] private ComputeShader lbmSolver;
    [SerializeField] private ComputeShader particleSolver;
    [SerializeField] private Material particleMaterial;

    [Header("Simulation Settings")]
    [SerializeField] private int width = 64;
    [SerializeField] private int height = 32;
    [SerializeField] private int depth = 32;
    [SerializeField] [Range(0.51f, 1.9f)] private float tau = 0.55f;

    [Header("Flow & Obstacle")]
    [SerializeField] private float initialVelocityX = 0.1f;
    [SerializeField] private Vector3 obstaclePos = new Vector3(20, 16, 16);
    [SerializeField] private float brushSize = 5.0f;

    [Header("Particle Settings")]
    [SerializeField] private int particleCount = 200000; // 20万パーティクル
    [SerializeField] private float particleSpeed = 300.0f; // 描画上の移動速度

    // バッファ類
    private ComputeBuffer bufferIn, bufferOut, obstacleBuffer;
    private ComputeBuffer particleBuffer;
    private RenderTexture resultTexture3D;

    private int kernelLbmStep, kernelAddObstacle, kernelUpdateParticles;

    void Start()
    {
        // 1. カーネルの取得
        kernelLbmStep = lbmSolver.FindKernel("LbmStep");
        kernelAddObstacle = lbmSolver.FindKernel("AddObstacle");
        kernelUpdateParticles = particleSolver.FindKernel("UpdateParticles");

        // 2. 3Dテクスチャの生成（流体の風向き保存用）
        resultTexture3D = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBFloat);
        resultTexture3D.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
        resultTexture3D.volumeDepth = depth;
        resultTexture3D.enableRandomWrite = true;
        resultTexture3D.filterMode = FilterMode.Bilinear;
        resultTexture3D.wrapMode = TextureWrapMode.Clamp;
        resultTexture3D.Create();

        // 3. 流体計算用バッファの確保
        int totalCells = width * height * depth;
        bufferIn = new ComputeBuffer(totalCells * 19, sizeof(float));
        bufferOut = new ComputeBuffer(totalCells * 19, sizeof(float));
        
        obstacleBuffer = new ComputeBuffer(totalCells, sizeof(int));
        obstacleBuffer.SetData(new int[totalCells]);

        // D3Q19の初期データを投入
        InitializeLbmData();

        // 静的パラメータのセットと障害物の配置
        lbmSolver.SetInt("width", width);
        lbmSolver.SetInt("height", height);
        lbmSolver.SetInt("depth", depth);
        lbmSolver.SetTexture(kernelLbmStep, "Result", resultTexture3D);
        
        lbmSolver.SetVector("obstaclePos", obstaclePos);
        lbmSolver.SetFloat("brushSize", brushSize);
        lbmSolver.SetBuffer(kernelAddObstacle, "obstacles", obstacleBuffer);
        lbmSolver.Dispatch(kernelAddObstacle, Mathf.CeilToInt(width/8f), Mathf.CeilToInt(height/8f), Mathf.CeilToInt(depth/8f));

        // 4. パーティクル用バッファの初期化
        particleBuffer = new ComputeBuffer(particleCount, 28);
        Particle[] initialParticles = new Particle[particleCount];
        for (int i = 0; i < particleCount; i++)
        {
            initialParticles[i].position = new Vector3(
                Random.Range(0f, width),
                Random.Range(0f, height),
                Random.Range(0f, depth)
            );
            initialParticles[i].velocity = Vector3.zero;
            initialParticles[i].life = Random.Range(0.0f, 4.0f);
        }
        particleBuffer.SetData(initialParticles);

        if (particleMaterial != null)
        {
            particleMaterial.SetBuffer("particlesBuffer", particleBuffer);
        }
    }

    void InitializeLbmData()
    {
        int totalCells = width * height * depth;
        float[] initialData = new float[totalCells * 19];

        float[] w = {
            1f / 3f,
            1f / 18f, 1f / 18f, 1f / 18f, 1f / 18f, 1f / 18f, 1f / 18f,
            1f / 36f, 1f / 36f, 1f / 36f, 1f / 36f, 1f / 36f, 1f / 36f, 1f / 36f, 1f / 36f, 1f / 36f, 1f / 36f, 1f / 36f, 1f / 36f
        };

        Vector3Int[] c = {
            new Vector3Int(0, 0, 0),
            new Vector3Int(1, 0, 0), new Vector3Int(-1, 0, 0), new Vector3Int(0, 1, 0), new Vector3Int(0, -1, 0), new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1),
            new Vector3Int(1, 1, 0), new Vector3Int(-1, -1, 0), new Vector3Int(1, -1, 0), new Vector3Int(-1, 1, 0),
            new Vector3Int(1, 0, 1), new Vector3Int(-1, 0, -1), new Vector3Int(1, 0, -1), new Vector3Int(-1, 0, 1),
            new Vector3Int(0, 1, 1), new Vector3Int(0, -1, -1), new Vector3Int(0, 1, -1), new Vector3Int(0, -1, 1)
        };

        float rho0 = 1.0f;
        Vector3 u0 = new Vector3(initialVelocityX, 0f, 0f);

        for (int z = 0; z < depth; z++)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float uSq = u0.x * u0.x + u0.y * u0.y + u0.z * u0.z;
                    int gridIdx = (z * height * width) + (y * width) + x;

                    for (int i = 0; i < 19; i++)
                    {
                        float cu = c[i].x * u0.x + c[i].y * u0.y + c[i].z * u0.z;
                        float feq = w[i] * rho0 * (1f + 3f * cu + 4.5f * cu * cu - 1.5f * uSq);

                        int index = gridIdx * 19 + i;
                        initialData[index] = feq;
                    }
                }
            }
        }

        bufferIn.SetData(initialData);
        bufferOut.SetData(initialData);
    }

    void Update()
    {
        // 1. 流体の計算 (LBM)
        lbmSolver.SetFloat("omega", 1.0f / tau);
        lbmSolver.SetFloat("inletVelocity", initialVelocityX);
        lbmSolver.SetBuffer(kernelLbmStep, "f_in", bufferIn);
        lbmSolver.SetBuffer(kernelLbmStep, "f_out", bufferOut);
        lbmSolver.SetBuffer(kernelLbmStep, "obstacles", obstacleBuffer);
        
        lbmSolver.Dispatch(kernelLbmStep, Mathf.CeilToInt(width/8f), Mathf.CeilToInt(height/8f), Mathf.CeilToInt(depth/8f));

        ComputeBuffer temp = bufferIn;
        bufferIn = bufferOut;
        bufferOut = temp;

        // 2. 粒子の計算 (Particle Solver)
        particleSolver.SetVector("gridSize", new Vector3(width, height, depth));
        particleSolver.SetFloat("deltaTime", Time.deltaTime);
        particleSolver.SetFloat("particleSpeed", particleSpeed);
        particleSolver.SetVector("randomSeed", new Vector2(Random.value, Random.value));
        
        particleSolver.SetTexture(kernelUpdateParticles, "VelocityField", resultTexture3D);
        particleSolver.SetBuffer(kernelUpdateParticles, "particles", particleBuffer);
        
        int particleThreadGroups = Mathf.CeilToInt(particleCount / 256f);
        particleSolver.Dispatch(kernelUpdateParticles, particleThreadGroups, 1, 1);
    }

    // GPUに直接描画命令を出す
    void OnRenderObject()
    {
        if (particleMaterial != null && particleBuffer != null)
        {
            particleMaterial.SetPass(0);
            Graphics.DrawProceduralNow(MeshTopology.Points, particleCount);
        }
    }

    void OnDestroy()
    {
        if (bufferIn != null) bufferIn.Release();
        if (bufferOut != null) bufferOut.Release();
        if (obstacleBuffer != null) obstacleBuffer.Release();
        if (particleBuffer != null) particleBuffer.Release();
        if (resultTexture3D != null) resultTexture3D.Release();
    }
}