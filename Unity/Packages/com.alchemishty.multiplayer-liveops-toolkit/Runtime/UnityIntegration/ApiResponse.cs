using UnityEngine.Networking;

namespace MultiplayerLiveOpsToolkit.UnityIntegration
{
    public class ApiResponse
    {
        public UnityWebRequest.Result Result;
        public long ResponseCode;
        public string Error;
        public string Text;
        public bool IsSuccess => Result == UnityWebRequest.Result.Success;

        public void CopyFrom(UnityWebRequest request)
        {
            Result = request.result;
            ResponseCode = request.responseCode;
            Error = request.error;
            Text = request.downloadHandler?.text;
        }
    }
}
