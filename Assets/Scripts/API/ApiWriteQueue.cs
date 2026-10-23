using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

public class ApiWriteQueue : MonoBehaviour
{
    private static ApiWriteQueue instance;
    public static ApiWriteQueue Instance
    {
        get
        {
            if (instance == null)
            {
                GameObject queueObject = new GameObject("ApiWriteQueue");
                instance = queueObject.AddComponent<ApiWriteQueue>();
                DontDestroyOnLoad(queueObject);
            }
            return instance;
        }
    }

    private readonly Queue<PendingWrite> pendingWrites = new();
    private bool isProcessing;

    private const int MAX_RETRIES = 3;
    private const float BASE_RETRY_DELAY = 2f;

    private struct PendingWrite
    {
        public Func<Action, Action<string>, IEnumerator> apiCall;
        public string description;
        public int attemptCount;
        public Action onSuccess;
        public Action<string> onFinalFailure;
    }

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void Enqueue(
        Func<Action, Action<string>, IEnumerator> apiCall,
        string description,
        Action onSuccess = null,
        Action<string> onFinalFailure = null)
    {
        pendingWrites.Enqueue(new PendingWrite
        {
            apiCall = apiCall,
            description = description,
            attemptCount = 0,
            onSuccess = onSuccess,
            onFinalFailure = onFinalFailure,
        });

        if (!isProcessing)
            StartCoroutine(ProcessQueue());
    }

    private IEnumerator ProcessQueue()
    {
        isProcessing = true;

        while (pendingWrites.Count > 0)
        {
            var write = pendingWrites.Dequeue();
            bool succeeded = false;
            string lastError = null;

            while (write.attemptCount < MAX_RETRIES && !succeeded)
            {
                if (write.attemptCount > 0)
                    yield return new WaitForSeconds(BASE_RETRY_DELAY * write.attemptCount);

                write.attemptCount++;
                bool complete = false;

                yield return write.apiCall(
                    () => { succeeded = true; complete = true; },
                    (string error) => { lastError = error; complete = true; }
                );

                while (!complete) yield return null;
            }

            if (succeeded)
            {
                write.onSuccess?.Invoke();
            }
            else
            {
#if UNITY_EDITOR
                Debug.LogError($"ApiWriteQueue: '{write.description}' failed after {MAX_RETRIES} attempts: {lastError}");
#endif
                write.onFinalFailure?.Invoke(
                    $"'{write.description}' failed after {MAX_RETRIES} attempts: {lastError}");
            }
        }

        isProcessing = false;
    }

    private void OnApplicationPause(bool isPaused)
    {
        if (!isPaused && pendingWrites.Count > 0 && !isProcessing)
            StartCoroutine(ProcessQueue());
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }
}
