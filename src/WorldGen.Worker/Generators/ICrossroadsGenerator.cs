namespace WorldGen.Worker.Generators;

public interface ICrossroadsGenerator
{
    Task GenerateAsync(CancellationToken ct);
}
