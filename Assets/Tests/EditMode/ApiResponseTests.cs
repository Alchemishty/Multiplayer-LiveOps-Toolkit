using NUnit.Framework;
using UnityEngine.Networking;

[TestFixture]
public class ApiResponseTests
{
    [Test]
    public void IsSuccess_WhenResultIsSuccess_ReturnsTrue()
    {
        var apiResponse = new ApiResponse
        {
            Result = UnityWebRequest.Result.Success
        };

        Assert.IsTrue(apiResponse.IsSuccess);
    }

    [Test]
    public void IsSuccess_WhenResultIsConnectionError_ReturnsFalse()
    {
        var apiResponse = new ApiResponse
        {
            Result = UnityWebRequest.Result.ConnectionError
        };

        Assert.IsFalse(apiResponse.IsSuccess);
    }

    [Test]
    public void IsSuccess_WhenResultIsProtocolError_ReturnsFalse()
    {
        var apiResponse = new ApiResponse
        {
            Result = UnityWebRequest.Result.ProtocolError
        };

        Assert.IsFalse(apiResponse.IsSuccess);
    }

    [Test]
    public void IsSuccess_WhenResultIsDataProcessingError_ReturnsFalse()
    {
        var apiResponse = new ApiResponse
        {
            Result = UnityWebRequest.Result.DataProcessingError
        };

        Assert.IsFalse(apiResponse.IsSuccess);
    }

    [Test]
    public void DefaultState_IsNotSuccess()
    {
        var apiResponse = new ApiResponse();

        Assert.IsFalse(apiResponse.IsSuccess);
        Assert.AreEqual(0, apiResponse.ResponseCode);
        Assert.IsNull(apiResponse.Error);
        Assert.IsNull(apiResponse.Text);
    }

    [Test]
    public void CopyFrom_SuccessfulRequest_CopiesAllFields()
    {
        var request = UnityWebRequest.Get("https://localhost/test");
        request.downloadHandler = new DownloadHandlerBuffer();

        var apiResponse = new ApiResponse();
        apiResponse.CopyFrom(request);

        Assert.AreEqual(request.result, apiResponse.Result);
        Assert.AreEqual(request.responseCode, apiResponse.ResponseCode);
        Assert.AreEqual(request.error, apiResponse.Error);

        request.Dispose();
    }

    [Test]
    public void CopyFrom_RequestWithNoDownloadHandler_SetsTextToNull()
    {
        var request = new UnityWebRequest("https://localhost/test", "GET");

        var apiResponse = new ApiResponse();
        apiResponse.CopyFrom(request);

        Assert.IsNull(apiResponse.Text);

        request.Dispose();
    }

    [Test]
    public void CopyFrom_OverwritesPreviousState()
    {
        var apiResponse = new ApiResponse
        {
            Result = UnityWebRequest.Result.Success,
            ResponseCode = 200,
            Error = null,
            Text = "old data"
        };

        var request = new UnityWebRequest("https://localhost/test", "GET");
        apiResponse.CopyFrom(request);

        Assert.AreEqual(UnityWebRequest.Result.InProgress, apiResponse.Result);
        Assert.AreEqual(0, apiResponse.ResponseCode);
        Assert.IsNull(apiResponse.Text);

        request.Dispose();
    }
}
