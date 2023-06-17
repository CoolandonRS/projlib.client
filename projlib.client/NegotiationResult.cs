namespace CoolandonRS.projlib.client; 

public class NegotiationResult {
    private enum Result {
        Success, Failure, Data
    }

    public enum DataType {
        Bytes, String
    }

    private byte[]? bytes;
    private string? str;
    private Result status;

    /// <summary>
    /// Returns if a Negotiation was successful
    /// </summary>
    /// <returns>True if success/data given, false if failure</returns>
    public bool WasSuccessful() {
        return status != Result.Failure;
    }

    /// <summary>
    /// </summary>
    /// <returns>The attached data type, or null if no attached data.</returns>
    public DataType? HasData() {
        if (status != Result.Data) return null;
        if (bytes != null) return DataType.Bytes;
        if (str != null) return DataType.String;
        throw new InvalidOperationException("What a Terrible Failure");
    }

    public byte[] GetBytes() {
        if (bytes == null) throw new InvalidOperationException("No attached bytes");
        return bytes;
    }

    public string GetString() {
        if (str == null) throw new InvalidOperationException("No attached string");
        return str;
    }

    public NegotiationResult(bool success) {
        status = success ? Result.Success : Result.Failure;
    }

    public NegotiationResult(string str) {
        status = Result.Data;
        this.str = str;
    }

    public NegotiationResult(byte[] bytes) {
        status = Result.Data;
        this.bytes = bytes;
    }
}