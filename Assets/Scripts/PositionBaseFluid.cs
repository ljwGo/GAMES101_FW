using UnityEngine;
struct GridCounter {
    public uint Count;
    public uint Iter;
};

public class PositionBaseFluid : MonoBehaviour
{
    public ComputeShader posBaseFluid;
    public Mesh drawMesh;
    public Material drawMaterial;
    public int positionFixedIter;

    const int ELEMENT_COUNT_PER_GROUP = 64;
    const int MORE_COUNT_PER_GROUP = 1024;
    const int MAX_NEIGHBOR_COUNT = 50;

    [SerializeField] Vector3 BoundaryBoxDiagonalMin;
    [SerializeField] Vector3 BoundaryBoxDiagonalMax;
    [SerializeField] Vector3 GenerateBoxDiagonalMin;
    [SerializeField] Vector3 GenerateBoxDiagonalMax;
    [SerializeField] Vector3 ExternForce;

    [SerializeField] float ParticleRadius;
    [SerializeField] float TimeInterval;
    [SerializeField] float SoftCoefficient;
    [SerializeField] float XSPHCoe = 0.01f;
    [SerializeField] float ScorrCoek = 0.1f;
    [SerializeField] float ScorrCoep = 0.2f;
    [SerializeField] int ScorrCoen = 4;

    [Range(1, 10)]
    [SerializeField] uint ratio = 1;

    Vector3Int GridSize;

    int ParticleSum;
    int GridSum;
    int ParticleGroupCount;  // dispatch
    int GridGroupCount;

    float particleDiameter;
    //float smoothKernelDiameter;
    // 看了别人完成的代码，平滑核半径和粒子半径成比例
    float smoothKernelRadius;
    //float smoothKernelRadius2;
    // 别人的代码使用了一种方式自动计算静态密度
    float staticDensity;
    float cubeGridEdgeLen;

    // You need to assign ComputerBuffer to computer shader. You don't must to assign data for it.
    ComputeBuffer _PositionBuffer;
    ComputeBuffer _PredictPositionBuffer;
    ComputeBuffer _VelocityBuffer;
    ComputeBuffer _ParticleCountInGridBuffer;  // test ok
    ComputeBuffer _PrefixSumInGridBuffer;  // test ok
    ComputeBuffer _ParticlesIxInGridBuffer;  // test ok
    ComputeBuffer _NeighborCounterBuffer;
    ComputeBuffer _NeighborIxContainerBuffer;
    ComputeBuffer _LambdaCoefficientBuffer;
    ComputeBuffer _PositionOffsetBuffer;
    ComputeBuffer _XSPHVelocityBuffer;

    // Debug:
    ComputeBuffer _TestDataBuffer;
    float[] _TestData;

    // data for ComputerBuffer
    Vector3[] _Position;
    Vector3[] _Velocity;

    Vector3[] _TestPosition;
    Vector3[] _TestPredictPosition;
    Vector3[] _TestVelocity;
    GridCounter[] _TestParticleCountInGrid;
    int[] _TestPrefixSumInGrid;
    int[] _TestParticlesIxInGrid;
    int[] _TestNeighborCounter;
    int[] _TestNeighborIxContainer;
    float[] _TestLambdaCoefficient;
    Vector3[] _TestPositionOffset;
    Vector3[] _TestXSPHVelocity;

    Bounds drawBounds;

    int InitGridKernel;
    int InitParticleKernel;
    int ImplictEulerKernel;
    int CalcParticleCountInGridKernel;
    int CalcPrefixCountPerGridKernel;
    int ParticleEnterGridKernel;
    int FindNeighborKernel;
    int CalcLambdaKernel;
    int PosOffsetCalcAndApplyKernel;
    int UpdateSpeedKernel;
    int CalcXSPHSpeedKernel;
    int UpdateXSPHSpeedKernel;
    int UpdatePredictPositionKernel;

    void Start()
    {
        Init();

        //_InitTestArray();
        //_InitTestBuffer();
    }

    void Update()
    {
        Simulate();

        //TestForData();
        //ShowDebugInfo();
    }

    private void OnDrawGizmos() {
        Gizmos.color = Color.red;
        Gizmos.DrawWireCube((BoundaryBoxDiagonalMax + BoundaryBoxDiagonalMin) / 2, BoundaryBoxDiagonalMax - BoundaryBoxDiagonalMin);

        Gizmos.color = Color.green;
        Gizmos.DrawWireCube((GenerateBoxDiagonalMax + GenerateBoxDiagonalMin) / 2, GenerateBoxDiagonalMax - GenerateBoxDiagonalMin);
    }

    private void OnDisable() {
        if (_PositionBuffer != null) _PositionBuffer.Release();
        if (_PredictPositionBuffer != null) _PredictPositionBuffer.Release();
        if (_VelocityBuffer != null) _VelocityBuffer.Release();
        if (_ParticleCountInGridBuffer != null) _ParticleCountInGridBuffer.Release();
        if (_PrefixSumInGridBuffer != null) _PrefixSumInGridBuffer.Release();
        if (_ParticlesIxInGridBuffer != null) _ParticlesIxInGridBuffer.Release();
        if (_NeighborCounterBuffer != null) _NeighborCounterBuffer.Release();
        if (_NeighborIxContainerBuffer != null) _NeighborIxContainerBuffer.Release();
        if (_LambdaCoefficientBuffer != null) _LambdaCoefficientBuffer.Release();
        if (_PositionOffsetBuffer != null) _PositionOffsetBuffer.Release();
        if (_XSPHVelocityBuffer != null) _XSPHVelocityBuffer.Release();
        if (_TestDataBuffer != null) _TestDataBuffer.Release();
    }

    void Init() {
        // 这里使用例子半径的整数倍作为网格宽度

        cubeGridEdgeLen = ParticleRadius * ratio;

        _InitGrid(cubeGridEdgeLen);
        _InitParticle();
        _InitKernel();
        _InitOther();
        _InitBuffer();
        _SetDataForBuffer();
        _InitVariables();
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
        InitGridKernel = posBaseFluid.FindKernel("InitGrid");
        InitParticleKernel = posBaseFluid.FindKernel("InitParticle");
        ImplictEulerKernel = posBaseFluid.FindKernel("ImplictEuler");
        CalcParticleCountInGridKernel = posBaseFluid.FindKernel("CalcParticleCountInGrid");
        CalcPrefixCountPerGridKernel = posBaseFluid.FindKernel("CalcPrefixCountPerGrid");
        ParticleEnterGridKernel = posBaseFluid.FindKernel("ParticleEnterGrid");
        FindNeighborKernel = posBaseFluid.FindKernel("FindNeighbor");
        CalcLambdaKernel = posBaseFluid.FindKernel("CalcLambda");
        PosOffsetCalcAndApplyKernel = posBaseFluid.FindKernel("PosOffsetCalcAndApply");
        UpdateSpeedKernel = posBaseFluid.FindKernel("UpdateSpeed");
        CalcXSPHSpeedKernel = posBaseFluid.FindKernel("CalcXSPHSpeed");
        UpdateXSPHSpeedKernel = posBaseFluid.FindKernel("UpdateXSPHSpeed");
        UpdatePredictPositionKernel = posBaseFluid.FindKernel("UpdatePredictPosition");
    }

    void _InitOther() {
        drawBounds = new Bounds(
            (BoundaryBoxDiagonalMax + BoundaryBoxDiagonalMin) * 0.5f, 
            BoundaryBoxDiagonalMax - BoundaryBoxDiagonalMin + new Vector3(particleDiameter, particleDiameter, particleDiameter));
        smoothKernelRadius = ParticleRadius * ratio;
        ParticleGroupCount = ParticleSum / ELEMENT_COUNT_PER_GROUP + 1;
        GridGroupCount = GridSum / ELEMENT_COUNT_PER_GROUP + 1;
        // 9 * 315.0f / 64 / Mathf.PI / (radius * ratio) / (radius * ratio) / (radius * ratio) * (5.0f / 9) * (5.0f / 9) * (5.0f / 9)
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
        _PositionOffsetBuffer = new ComputeBuffer(ParticleSum, 4 * 3);
        // bug02: not is (ParticleSum, 4 * MAX_NEIGHBOR_COUNT);
        _NeighborIxContainerBuffer = new ComputeBuffer(ParticleSum * MAX_NEIGHBOR_COUNT, 4);
        _LambdaCoefficientBuffer = new ComputeBuffer(ParticleSum, 4);
        //_PositionOffsetBuffer = new ComputeBuffer(ParticleSum, 4 * 3);
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
        posBaseFluid.SetInt("ScorrCoen", ScorrCoen);
        posBaseFluid.SetFloat("CubeGridEdgeLen", cubeGridEdgeLen);
        posBaseFluid.SetFloat("SmoothKernelRadius", smoothKernelRadius);
        posBaseFluid.SetFloat("StaticDensity", staticDensity);
        posBaseFluid.SetFloat("TimeInterval", TimeInterval);
        posBaseFluid.SetFloat("XSPHCoe", XSPHCoe);
        posBaseFluid.SetFloat("Radius", ParticleRadius);
        posBaseFluid.SetFloat("ScorrCoek", ScorrCoek);
        posBaseFluid.SetFloat("ScorrCoep", ScorrCoep);
        posBaseFluid.SetFloat("SoftCoefficient", SoftCoefficient);
        posBaseFluid.SetInts("GridSize", GridSize.x, GridSize.y, GridSize.z);
        posBaseFluid.SetFloats("ExternForce", ExternForce.x, ExternForce.y, ExternForce.z);
        posBaseFluid.SetFloats("BoundaryBoxDiagonalMin", BoundaryBoxDiagonalMin.x, BoundaryBoxDiagonalMin.y, BoundaryBoxDiagonalMin.z);
        posBaseFluid.SetFloats("BoundaryBoxDiagonalMax", BoundaryBoxDiagonalMax.x, BoundaryBoxDiagonalMax.y, BoundaryBoxDiagonalMax.z);

        // I don't know why per buffer has to assign for per kernel
        posBaseFluid.SetBuffer(InitGridKernel, "_ParticleCountInGrid", _ParticleCountInGridBuffer);

        posBaseFluid.SetBuffer(InitParticleKernel, "_NeighborCounter", _NeighborCounterBuffer);

        posBaseFluid.SetBuffer(ImplictEulerKernel, "_Velocity", _VelocityBuffer);
        posBaseFluid.SetBuffer(ImplictEulerKernel, "_PredictPosition", _PredictPositionBuffer);
        posBaseFluid.SetBuffer(ImplictEulerKernel, "_Position", _PositionBuffer);

        posBaseFluid.SetBuffer(CalcParticleCountInGridKernel, "_PredictPosition", _PredictPositionBuffer);
        posBaseFluid.SetBuffer(CalcParticleCountInGridKernel, "_ParticleCountInGrid", _ParticleCountInGridBuffer);

        posBaseFluid.SetBuffer(CalcPrefixCountPerGridKernel, "_ParticleCountInGrid", _ParticleCountInGridBuffer);
        posBaseFluid.SetBuffer(CalcPrefixCountPerGridKernel, "_PrefixSumInGrid", _PrefixSumInGridBuffer);

        posBaseFluid.SetBuffer(ParticleEnterGridKernel, "_PredictPosition", _PredictPositionBuffer);
        posBaseFluid.SetBuffer(ParticleEnterGridKernel, "_ParticleCountInGrid", _ParticleCountInGridBuffer);
        posBaseFluid.SetBuffer(ParticleEnterGridKernel, "_ParticlesIxInGrid", _ParticlesIxInGridBuffer);
        posBaseFluid.SetBuffer(ParticleEnterGridKernel, "_PrefixSumInGrid", _PrefixSumInGridBuffer);

        posBaseFluid.SetBuffer(FindNeighborKernel, "_PredictPosition", _PredictPositionBuffer);
        posBaseFluid.SetBuffer(FindNeighborKernel, "_ParticlesIxInGrid", _ParticlesIxInGridBuffer);
        posBaseFluid.SetBuffer(FindNeighborKernel, "_PrefixSumInGrid", _PrefixSumInGridBuffer);
        posBaseFluid.SetBuffer(FindNeighborKernel, "_ParticleCountInGrid", _ParticleCountInGridBuffer);
        posBaseFluid.SetBuffer(FindNeighborKernel, "_NeighborCounter", _NeighborCounterBuffer);
        posBaseFluid.SetBuffer(FindNeighborKernel, "_NeighborIxContainer", _NeighborIxContainerBuffer);

        posBaseFluid.SetBuffer(CalcLambdaKernel, "_NeighborCounter", _NeighborCounterBuffer);
        posBaseFluid.SetBuffer(CalcLambdaKernel, "_NeighborIxContainer", _NeighborIxContainerBuffer);
        posBaseFluid.SetBuffer(CalcLambdaKernel, "_PredictPosition", _PredictPositionBuffer);
        posBaseFluid.SetBuffer(CalcLambdaKernel, "_LambdaCoefficient", _LambdaCoefficientBuffer);

        posBaseFluid.SetBuffer(PosOffsetCalcAndApplyKernel, "_NeighborCounter", _NeighborCounterBuffer);
        posBaseFluid.SetBuffer(PosOffsetCalcAndApplyKernel, "_NeighborIxContainer", _NeighborIxContainerBuffer);
        posBaseFluid.SetBuffer(PosOffsetCalcAndApplyKernel, "_PredictPosition", _PredictPositionBuffer);
        posBaseFluid.SetBuffer(PosOffsetCalcAndApplyKernel, "_LambdaCoefficient", _LambdaCoefficientBuffer);
        posBaseFluid.SetBuffer(PosOffsetCalcAndApplyKernel, "_PositionOffset", _PositionOffsetBuffer);

        posBaseFluid.SetBuffer(UpdateSpeedKernel, "_Position", _PositionBuffer);
        posBaseFluid.SetBuffer(UpdateSpeedKernel, "_Velocity", _VelocityBuffer);
        posBaseFluid.SetBuffer(UpdateSpeedKernel, "_PredictPosition", _PredictPositionBuffer);

        posBaseFluid.SetBuffer(CalcXSPHSpeedKernel, "_Velocity", _VelocityBuffer);
        posBaseFluid.SetBuffer(CalcXSPHSpeedKernel, "_PredictPosition", _PredictPositionBuffer);
        posBaseFluid.SetBuffer(CalcXSPHSpeedKernel, "_NeighborCounter", _NeighborCounterBuffer);
        posBaseFluid.SetBuffer(CalcXSPHSpeedKernel, "_NeighborIxContainer", _NeighborIxContainerBuffer);
        posBaseFluid.SetBuffer(CalcXSPHSpeedKernel, "_XSPHVelocity", _XSPHVelocityBuffer);

        posBaseFluid.SetBuffer(UpdateXSPHSpeedKernel, "_Velocity", _VelocityBuffer);
        posBaseFluid.SetBuffer(UpdateXSPHSpeedKernel, "_XSPHVelocity", _XSPHVelocityBuffer);
        posBaseFluid.SetBuffer(UpdateXSPHSpeedKernel, "_Position", _PositionBuffer);
        posBaseFluid.SetBuffer(UpdateXSPHSpeedKernel, "_PredictPosition", _PredictPositionBuffer);

        posBaseFluid.SetBuffer(UpdatePredictPositionKernel, "_PredictPosition", _PredictPositionBuffer);
        posBaseFluid.SetBuffer(UpdatePredictPositionKernel, "_PositionOffset", _PositionOffsetBuffer);

        drawMaterial.SetBuffer("_Positions", _PositionBuffer);
        drawMaterial.SetFloat("particleRadius", ParticleRadius);
    }

    void Simulate() {
        posBaseFluid.Dispatch(InitGridKernel, GridGroupCount, 1, 1);
        posBaseFluid.Dispatch(InitParticleKernel, ParticleGroupCount, 1, 1);
        posBaseFluid.Dispatch(ImplictEulerKernel, ParticleGroupCount, 1, 1);
        posBaseFluid.Dispatch(CalcParticleCountInGridKernel, ParticleGroupCount, 1, 1);
        posBaseFluid.Dispatch(CalcPrefixCountPerGridKernel, 1, 1, 1);
        posBaseFluid.Dispatch(ParticleEnterGridKernel, ParticleGroupCount, 1, 1);
        posBaseFluid.Dispatch(FindNeighborKernel, ParticleGroupCount, 1, 1);

        for (int i = 0; i < positionFixedIter; ++i) {
            posBaseFluid.Dispatch(CalcLambdaKernel, ParticleGroupCount, 1, 1);

            //TestForData();
            posBaseFluid.Dispatch(PosOffsetCalcAndApplyKernel, ParticleGroupCount, 1, 1);
            //TestForData();
            //posBaseFluid.Dispatch(UpdatePredictPositionKernel, ParticleGroupCount, 1, 1);
        }

        posBaseFluid.Dispatch(UpdateSpeedKernel, ParticleGroupCount, 1, 1);
        posBaseFluid.Dispatch(CalcXSPHSpeedKernel, ParticleGroupCount, 1, 1);
        posBaseFluid.Dispatch(UpdateXSPHSpeedKernel, ParticleGroupCount, 1, 1);
        //TestForData();


        Graphics.DrawMeshInstancedProcedural(drawMesh, 0, drawMaterial, drawBounds, ParticleSum);
    }

    // Debug:

    void _InitTestArray() {
        _TestParticlesIxInGrid = new int[ParticleSum];
        _TestPrefixSumInGrid = new int[GridSum];
        _TestPosition = new Vector3[ParticleSum];
        _TestPredictPosition = new Vector3[ParticleSum];
        _TestVelocity = new Vector3[ParticleSum];
        _TestParticleCountInGrid = new GridCounter[GridSum];
        _TestNeighborCounter = new int[ParticleSum];
        _TestNeighborIxContainer = new int[ParticleSum * MAX_NEIGHBOR_COUNT];
        _TestXSPHVelocity = new Vector3[ParticleSum];
        _TestLambdaCoefficient = new float[ParticleSum];
        _TestPositionOffset = new Vector3[ParticleSum];

        _TestData = new float[ParticleSum * MAX_NEIGHBOR_COUNT];
        _TestDataBuffer = new ComputeBuffer(ParticleSum * MAX_NEIGHBOR_COUNT, 4);
    }

    void TestForData() {
        _ParticleCountInGridBuffer.GetData(_TestParticleCountInGrid);
        _PrefixSumInGridBuffer.GetData(_TestPrefixSumInGrid);
        _ParticlesIxInGridBuffer.GetData(_TestParticlesIxInGrid);
        _NeighborCounterBuffer.GetData(_TestNeighborCounter);
        _NeighborIxContainerBuffer.GetData(_TestNeighborIxContainer);
        _LambdaCoefficientBuffer.GetData(_TestLambdaCoefficient);
        _PositionBuffer.GetData(_TestPosition);
        _VelocityBuffer.GetData(_TestVelocity);
        _PredictPositionBuffer.GetData(_TestPredictPosition);
        _XSPHVelocityBuffer.GetData(_TestXSPHVelocity);

        _TestDataBuffer.GetData(_TestData);

        _PositionOffsetBuffer.GetData(_TestPositionOffset);
    }

    void _InitTestBuffer() {
        posBaseFluid.SetBuffer(CalcLambdaKernel, "_TestData", _TestDataBuffer);
        posBaseFluid.SetBuffer(PosOffsetCalcAndApplyKernel, "_TestData", _TestDataBuffer);
        posBaseFluid.SetBuffer(FindNeighborKernel, "_TestData", _TestDataBuffer);
    }

    void ShowDebugInfo() {
        Debug.Log("ParticleSum: " + ParticleSum);
        Debug.Log("GridSum: " + GridSum);

        uint sum = 0;
        foreach (GridCounter count in _TestParticleCountInGrid) {
            sum += count.Count;
        }
        Debug.Log("ParticleSumInGrid: " + sum);

        int maxSum = 0;
        int maxGridId = 0;
        int i = 0;
        foreach (int prefix in _TestPrefixSumInGrid) {
            if (maxSum <  prefix) {
                maxSum = prefix;
                maxGridId = i;
            }
            i++;
        }
        Debug.Log("MaxPrefixSum: " + maxSum);
        Debug.Log("MaxPrefixSum_GridId: " + maxGridId);

        bool ixHasSame = false;
        for (i = 0; i < _TestParticlesIxInGrid.Length; ++i){
            for (int j = i + 1; j < _TestParticlesIxInGrid.Length; ++j) {
                if (_TestParticlesIxInGrid[i] == _TestParticlesIxInGrid[j]) {
                    ixHasSame = true; break;
                }
            }
        }
        Debug.Log(ixHasSame ? "Error! One particle in two different ix" : "particle ix is all different!");


    }
}