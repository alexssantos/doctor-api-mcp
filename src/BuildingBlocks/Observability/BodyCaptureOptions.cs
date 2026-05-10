namespace McpApis.BuildingBlocks.Observability;

public interface IBodyCaptureOptions
{
    bool Enabled { get; }
}

public class BodyCaptureOptions : IBodyCaptureOptions
{
    public bool Enabled { get; set; }
}
