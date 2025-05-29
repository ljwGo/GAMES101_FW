using Unity.VisualScripting;
using UnityEngine;

public class OptPosBaseFluid : MonoBehaviour
{
    public ComputeShader posBaseFluid;
    public Mesh drawMesh;
    public Material drawMaterial;

    const int MAX_NEIGHBOR_COUNT = 50;

    [SerializeField] Vector3 BoundaryBoxDiagonalMin;
    [SerializeField] Vector3 BoundaryBoxDiagonalMax;
    [SerializeField] Vector3 GenerateBoxDiagonalMin;
    [SerializeField] Vector3 GenerateBoxDiagonalMax;
    [SerializeField] Vector3 ExternForce;

    [SerializeField] float ParticleRadius;
    [SerializeField] float DeltaT;
    [SerializeField] float SoftCoefficient;
    [SerializeField] float XSPHCoef = 0.01f;
    [SerializeField] float ScorrCoefK = 0.1f;
    [SerializeField] float ScorrCoefP = 0.2f;
    [SerializeField] int ScorrCoefN = 4;

    [Range(1, 10)]
    [SerializeField] uint ratio = 1;

    Vector3Int GridSize;

    int ParticleSum;
    int GridSum;
    int BlockNum = 1;

    float InvDeltaT;
    float particleDiameter;
    float smoothKernelRadius;
    float staticDensity;
    float cubeGridEdgeLen;

    ComputeBuffer _PositionBuffer;
    ComputeBuffer _PredictPositionBuffer;
    ComputeBuffer _VelocityBuffer;
    ComputeBuffer _ParticleCountInGridBuffer;
    ComputeBuffer _PrefixSumInGridBuffer;
    ComputeBuffer _ParticlesIxInGridBuffer;
    ComputeBuffer _NeighborCounterBuffer;
    ComputeBuffer _NeighborIxContainerBuffer;
    ComputeBuffer _LambdaCoefficientBuffer;
    ComputeBuffer _XSPHVelocityBuffer;

    // data for ComputerBuffer
    Vector3[] _Position;
    Vector3[] _Velocity;

    Bounds drawBounds;

    int Step1;
    int Step2;
    int Step3;
    int SolveInOneKernel;

    void Start()
    {
        cubeGridEdgeLen = ParticleRadius * ratio;

        _InitGrid(cubeGridEdgeLen);
        _InitParticle();
        _InitKernel();
        _InitOther();
        _InitBuffer();
        _SetDataForBuffer();
        _InitVariables();
    }
    void Update()
    {

        Graphics.DrawMeshInstancedProcedural(drawMesh, 0, drawMaterial, drawBounds, ParticleSum);

        BlockNum = (ParticleSum - 1) / 1024 + 1;
        posBaseFluid.SetInt("BlockNum", BlockNum);
        posBaseFluid.Dispatch(Step1, BlockNum, 1, 1);

        BlockNum = 1;
        posBaseFluid.SetInt("blockNumx", BlockNum);
        posBaseFluid.Dispatch(Step2, BlockNum, 1, 1);

        BlockNum = (ParticleSum - 1) / 1024 + 1;
        posBaseFluid.SetInt("blockNumx", BlockNum);
        posBaseFluid.Dispatch(Step3, BlockNum, 1, 1);

        BlockNum = (ParticleSum - 1) / 1024 + 1;
        posBaseFluid.SetInt("blockNumx", BlockNum);
        posBaseFluid.Dispatch(SolveInOneKernel, BlockNum, 1, 1);
    }

    private void OnDrawGizmos() {
        Gizmos.color = Color.red;
        Gizmos.DrawWireCube((BoundaryBoxDiagonalMax + BoundaryBoxDiagonalMin) / 2, BoundaryBoxDiagonalMax - BoundaryBoxDiagonalMin);

        Gizmos.color = Color.green;
        Gizmos.DrawWireCube((GenerateBoxDiagonalMax + GenerateBoxDiagonalMin) / 2, GenerateBoxDiagonalMax - GenerateBoxDiagonalMin);
    }

    void _InitGrid(float cubeGridEdgeSize) {
        int xCount = Mathf.CeilToInt((BoundaryBoxDiagonalMax.x - BoundaryBoxDiagonalMin.x) / cubeGridEdgeSize);
        int yCount = Mathf.CeilToInt((BoundaryBoxDiagonalMax.y - BoundaryBoxDiagonalMin.y) / cubeGridEdgeSize);
        int zCount = Mathf.CeilToInt((BoundaryBoxDiagonalMax.z - BoundaryBoxDiagonalMin.z) / cubeGridEdgeSize);

        GridSum = xCount * yCount * zCount;
        GridSize.x = xCount;
        GridSize.y = yCount;
        GridSize.z = zCount;
    }

    void _InitParticle() {
        particleDiameter = ParticleRadius * 2;

        int xCount = (int)((GenerateBoxDiagonalMax.x - GenerateBoxDiagonalMin.x) / particleDiameter);
        int yCount = (int)((GenerateBoxDiagonalMax.y - GenerateBoxDiagonalMin.y) / particleDiameter);
        int zCount = (int)((GenerateBoxDiagonalMax.z - GenerateBoxDiagonalMin.z) / particleDiameter);

        ParticleSum = xCount * yCount * zCount;

        _Position = new Vector3[ParticleSum];
        _Velocity = new Vector3[ParticleSum];

        int ix = 0;
        for (int i = 0; i < xCount; ++i) {
            for (int j = 0; j < yCount; ++j) {
                for (int k = 0; k < zCount; ++k) {
                    Vector3 pos = new Vector3();
                    pos.x = GenerateBoxDiagonalMin.x + i * particleDiameter + ParticleRadius;
                    pos.y = GenerateBoxDiagonalMin.y + j * particleDiameter + ParticleRadius;
                    pos.z = GenerateBoxDiagonalMin.z + k * particleDiameter + ParticleRadius;

                    _Velocity[ix] = Vector3.zero;
                    // add tiny offset
                    _Position[ix++] = pos + new Vector3(Random.Range(-0.1f, 0.1f), Random.Range(-0.1f, 0.1f), Random.Range(-0.1f, 0.1f));
                }
            }
        }
    }

    void _InitKernel() {
        Step1 = posBaseFluid.FindKernel("Step1");
        Step2 = posBaseFluid.FindKernel("Step2");
        Step3 = posBaseFluid.FindKernel("Step3");
        SolveInOneKernel = posBaseFluid.FindKernel("SolveInOneKernel");
    }

    void _InitOther() {
        drawBounds = new Bounds(
            (BoundaryBoxDiagonalMax + BoundaryBoxDiagonalMin) * 0.5f,
            BoundaryBoxDiagonalMax - BoundaryBoxDiagonalMin + new Vector3(particleDiameter, particleDiameter, particleDiameter));
        InvDeltaT = 1.0f / DeltaT;
        smoothKernelRadius = ParticleRadius * ratio;
        staticDensity = 9 * 315.0f / 64 / Mathf.PI / (ParticleRadius * ratio) / (ParticleRadius * ratio) / (ParticleRadius * ratio) * (5.0f / 9) * (5.0f / 9) * (5.0f / 9);
    }

    void _InitBuffer() {
        _PositionBuffer = new ComputeBuffer(ParticleSum, 4 * 3);
        _PredictPositionBuffer = new ComputeBuffer(ParticleSum, 4 * 3);
        _VelocityBuffer = new ComputeBuffer(ParticleSum, 4 * 3);
        _ParticleCountInGridBuffer = new ComputeBuffer(GridSum, 4 * 2);
        _PrefixSumInGridBuffer = new ComputeBuffer(GridSum, 4);
        _ParticlesIxInGridBuffer = new ComputeBuffer(ParticleSum, 4);
        _NeighborCounterBuffer = new ComputeBuffer(ParticleSum, 4);
        _NeighborIxContainerBuffer = new ComputeBuffer(ParticleSum * MAX_NEIGHBOR_COUNT, 4);
        _LambdaCoefficientBuffer = new ComputeBuffer(ParticleSum, 4);
        _XSPHVelocityBuffer = new ComputeBuffer(ParticleSum, 4 * 3);
    }

    void _SetDataForBuffer() {
        _PositionBuffer.SetData(_Position);
        _PredictPositionBuffer.SetData(_Position);
        _VelocityBuffer.SetData(_Velocity);
    }

    void _InitVariables() {
        posBaseFluid.SetInt("ParticleSum", ParticleSum);
        posBaseFluid.SetInt("GridSum", GridSum);
        posBaseFluid.SetInt("BlockNum", BlockNum);
        posBaseFluid.SetInt("ScorrCoefN", ScorrCoefN);
        posBaseFluid.SetFloat("CubeGridEdgeLen", cubeGridEdgeLen);
        posBaseFluid.SetFloat("SmoothKernelRadius", smoothKernelRadius);
        posBaseFluid.SetFloat("StaticDensity", staticDensity);
        posBaseFluid.SetFloat("DeltaT", DeltaT);
        posBaseFluid.SetFloat("InvDeltaT", InvDeltaT);
        posBaseFluid.SetFloat("XSPHCoef", XSPHCoef);
        posBaseFluid.SetFloat("Radius", ParticleRadius);
        posBaseFluid.SetFloat("ScorrCoefK", ScorrCoefK);
        posBaseFluid.SetFloat("ScorrCoefP", ScorrCoefP);
        posBaseFluid.SetFloat("SoftConstrainCoef", SoftCoefficient);
        posBaseFluid.SetInts("GridSize", GridSize.x, GridSize.y, GridSize.z);
        posBaseFluid.SetFloats("ExternForce", ExternForce.x, ExternForce.y, ExternForce.z);
        posBaseFluid.SetFloats("BoundaryBoxDiagonalMin", BoundaryBoxDiagonalMin.x, BoundaryBoxDiagonalMin.y, BoundaryBoxDiagonalMin.z);
        posBaseFluid.SetFloats("BoundaryBoxDiagonalMax", BoundaryBoxDiagonalMax.x, BoundaryBoxDiagonalMax.y, BoundaryBoxDiagonalMax.z);

        posBaseFluid.SetBuffer(Step1, "_Velocity", _VelocityBuffer);
        posBaseFluid.SetBuffer(Step1, "_PredictPosition", _PredictPositionBuffer);
        posBaseFluid.SetBuffer(Step1, "_Position", _PositionBuffer);
        posBaseFluid.SetBuffer(Step1, "_CountGrid", _ParticleCountInGridBuffer);
        posBaseFluid.SetBuffer(Step1, "_PrefixSum", _PrefixSumInGridBuffer);

        posBaseFluid.SetBuffer(Step2, "_PrefixSum", _PrefixSumInGridBuffer);

        posBaseFluid.SetBuffer(Step3, "_PredictPosition", _PredictPositionBuffer);
        posBaseFluid.SetBuffer(Step3, "_CountGrid", _ParticleCountInGridBuffer);
        posBaseFluid.SetBuffer(Step3, "_PrefixSum", _PrefixSumInGridBuffer);
        posBaseFluid.SetBuffer(Step3, "_CountNeighbor", _NeighborCounterBuffer);
        posBaseFluid.SetBuffer(Step3, "_IxInGrid", _ParticlesIxInGridBuffer);
        posBaseFluid.SetBuffer(Step3, "_IxNeighbor", _NeighborIxContainerBuffer);

        posBaseFluid.SetBuffer(SolveInOneKernel, "_CountNeighbor", _NeighborCounterBuffer);
        posBaseFluid.SetBuffer(SolveInOneKernel, "_PredictPosition", _PredictPositionBuffer);
        posBaseFluid.SetBuffer(SolveInOneKernel, "_IxNeighbor", _NeighborIxContainerBuffer);
        posBaseFluid.SetBuffer(SolveInOneKernel, "_Lambda", _LambdaCoefficientBuffer);
        posBaseFluid.SetBuffer(SolveInOneKernel, "_Position", _PositionBuffer);
        posBaseFluid.SetBuffer(SolveInOneKernel, "_Velocity", _VelocityBuffer);
        posBaseFluid.SetBuffer(SolveInOneKernel, "_VelocityXSPH", _XSPHVelocityBuffer);

        drawMaterial.SetBuffer("_Positions", _PositionBuffer);
        drawMaterial.SetFloat("particleRadius", ParticleRadius);
    }
}
