namespace projupdate_client; 

public class UpdateClientException : InvalidOperationException {
    public UpdateClientException(string msg) : base(msg) {}
    public UpdateClientException() : base() { }
}