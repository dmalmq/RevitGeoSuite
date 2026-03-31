namespace RevitGeoSuite.Core.Mesh;

public interface IMeshCalculator
{
    MeshCode Calculate(double latitude, double longitude, JapanMeshLevel level = JapanMeshLevel.Tertiary);

    MeshCode CalculatePrimaryMesh(double latitude, double longitude);

    MeshBounds GetBounds(MeshCode meshCode);
}
