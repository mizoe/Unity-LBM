using UnityEngine;
using UnityEngine.InputSystem; // New Input System用

public class LbmController2D : MonoBehaviour
{
    [Header("Shader References")]
    [SerializeField] private ComputeShader lbmSolver;
    [SerializeField] private Material visualizerMaterial;

    [Header("Simulation Settings")]
    [SerializeField] private int width = 256;
    [SerializeField] private int height = 128;
    [SerializeField] [Range(0.51f, 1.9f)] private float tau = 0.6f; // 下限を0.51に変更（破綻防止）

    [Header("Initial Flow")]
    [SerializeField] private float initialVelocityX = 0.1f;

    [Header("Brush Settings")]
    [SerializeField] private float brushSize = 5.0f;

    private ComputeBuffer bufferIn;
    private ComputeBuffer bufferOut;
    private ComputeBuffer obstacleBuffer;
    private RenderTexture resultTexture;

    private int kernelLbmStep;
    private int kernelAddObstacle;
    private int threadGroupsX;
    private int threadGroupsY;

    void Start()
    {
        kernelLbmStep = lbmSolver.FindKernel("LbmStep");
        kernelAddObstacle = lbmSolver.FindKernel("AddObstacle");
        
        threadGroupsX = Mathf.CeilToInt(width / 8f);
        threadGroupsY = Mathf.CeilToInt(height / 8f);

        resultTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBFloat);
        resultTexture.enableRandomWrite = true;
        resultTexture.filterMode = FilterMode.Bilinear;
        resultTexture.wrapMode = TextureWrapMode.Repeat;
        resultTexture.Create();

        int elementCount = width * height * 9;
        bufferIn = new ComputeBuffer(elementCount, sizeof(float));
        bufferOut = new ComputeBuffer(elementCount, sizeof(float));
        
        obstacleBuffer = new ComputeBuffer(width * height, sizeof(int));
        int[] initialObstacles = new int[width * height];
        obstacleBuffer.SetData(initialObstacles);

        InitializeLbmData();

        lbmSolver.SetInt("width", width);
        lbmSolver.SetInt("height", height);
        lbmSolver.SetTexture(kernelLbmStep, "Result", resultTexture);

        if (visualizerMaterial != null)
        {
            visualizerMaterial.SetTexture("_MainTex", resultTexture);
        }
    }

    void InitializeLbmData()
    {
        int elementCount = width * height * 9;
        float[] initialData = new float[elementCount];

        float[] w = {
            4f / 9f,
            1f / 9f, 1f / 9f, 1f / 9f, 1f / 9f,
            1f / 36f, 1f / 36f, 1f / 36f, 1f / 36f
        };

        Vector2Int[] c = {
            new Vector2Int(0, 0),  new Vector2Int(1, 0),  new Vector2Int(0, 1), 
            new Vector2Int(-1, 0), new Vector2Int(0, -1), new Vector2Int(1, 1), 
            new Vector2Int(-1, 1), new Vector2Int(-1, -1), new Vector2Int(1, -1)
        };

        float rho0 = 1.0f;
        Vector2 u0 = new Vector2(initialVelocityX, 0f);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float uSq = u0.x * u0.x + u0.y * u0.y;

                for (int i = 0; i < 9; i++)
                {
                    float cu = c[i].x * u0.x + c[i].y * u0.y;
                    float feq = w[i] * rho0 * (1f + 3f * cu + 4.5f * cu * cu - 1.5f * uSq);

                    int index = (y * width + x) * 9 + i;
                    initialData[index] = feq;
                }
            }
        }

        bufferIn.SetData(initialData);
        bufferOut.SetData(initialData);
    }

    void Update()
    {
        // 1. マウス入力による壁の追加 (New Input System対応)
        if (Mouse.current != null && Mouse.current.leftButton.isPressed)
        {
            Vector2 mouseScreenPos = Mouse.current.position.ReadValue();
            Ray ray = Camera.main.ScreenPointToRay(mouseScreenPos);
            
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                Vector2 mousePos = new Vector2(hit.textureCoord.x * width, hit.textureCoord.y * height);
                
                lbmSolver.SetVector("mousePos", mousePos);
                lbmSolver.SetFloat("brushSize", brushSize);
                lbmSolver.SetBuffer(kernelAddObstacle, "obstacles", obstacleBuffer);
                
                lbmSolver.Dispatch(kernelAddObstacle, threadGroupsX, threadGroupsY, 1);
            }
        }

        // 2. 流体シミュレーションのステップ実行
        float omega = 1.0f / tau;
        lbmSolver.SetFloat("omega", omega);
        lbmSolver.SetFloat("inletVelocity", initialVelocityX); // 風洞の流速を渡す

        lbmSolver.SetBuffer(kernelLbmStep, "f_in", bufferIn);
        lbmSolver.SetBuffer(kernelLbmStep, "f_out", bufferOut);
        lbmSolver.SetBuffer(kernelLbmStep, "obstacles", obstacleBuffer);
        
        lbmSolver.Dispatch(kernelLbmStep, threadGroupsX, threadGroupsY, 1);

        // Ping-Pongバッファの入れ替え
        ComputeBuffer temp = bufferIn;
        bufferIn = bufferOut;
        bufferOut = temp;
    }

    void OnDestroy()
    {
        if (bufferIn != null) bufferIn.Release();
        if (bufferOut != null) bufferOut.Release();
        if (obstacleBuffer != null) obstacleBuffer.Release();
        if (resultTexture != null) resultTexture.Release();
    }
}