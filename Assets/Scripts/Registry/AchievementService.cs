using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;

public class AchievementService : MonoBehaviour
{
    public static AchievementService Instance { get; private set; }

    [SerializeField] private AchievementCatalogSO masterCatalog;

    private AchievementProgressTracker tracker = new();
    private IAchievementAuthority authority;
    private bool isLoaded;
    private float progressSyncTimer;
    private HashSet<string> dirtyProgressIds = new();
    private readonly Queue<PendingEvent> pendingEvents = new();

    private const float PROGRESS_SYNC_INTERVAL = 30f;

    private struct PendingEvent
    {
        public GameEventType eventType;
        public GameEventContext context;
    }

    public event Action<string> AchievementUnlocked;
    public event Action AchievementsLoaded;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (masterCatalog == null)
            throw new InvalidOperationException(
                "AchievementService: masterCatalog not assigned in inspector");

        transform.parent = null;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        authority = GetComponent<IAchievementAuthority>() as IAchievementAuthority;
        if (authority == null)
        {
            var serverValidatedAuthority = gameObject.AddComponent<ServerValidatedAuthority>();
            authority = serverValidatedAuthority;
        }

        StartCoroutine(Initialize());
    }

    private IEnumerator Initialize()
    {
        while (NetworkManager.Singleton == null)
            yield return null;

        while (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
            yield return null;

        if (!NetworkManager.Singleton.IsClient)
            yield break;

        yield return LoadAchievements();
    }

    private IEnumerator LoadAchievements()
    {
        AchievementDefinitionResponse definitionResponse = null;
        string definitionError = null;
        bool definitionRequestComplete = false;

        yield return AchievementAPI.Instance.GetDefinitions(
            (response) => { definitionResponse = response; definitionRequestComplete = true; },
            (error) => { definitionError = error; definitionRequestComplete = true; }
        );
        while (!definitionRequestComplete) yield return null;

        if (definitionError != null)
        {
            Debug.LogError($"AchievementService: failed to load definitions: {definitionError}");
            yield break;
        }

        var mergedDefinitions = MergeDefinitions(definitionResponse);
        RegisterEvaluators();
        tracker.SetDefinitions(mergedDefinitions);

        AchievementProgressResponse progressResponse = null;
        string progressError = null;
        bool progressRequestComplete = false;

        yield return AchievementAPI.Instance.GetProgress(
            (response) => { progressResponse = response; progressRequestComplete = true; },
            (error) => { progressError = error; progressRequestComplete = true; }
        );
        while (!progressRequestComplete) yield return null;

        if (progressError != null)
        {
            Debug.LogWarning($"AchievementService: failed to load progress: {progressError}");
        }
        else if (progressResponse?.progress != null)
        {
            var progressList = new List<AchievementProgress>();
            foreach (var progressDto in progressResponse.progress)
            {
                progressList.Add(new AchievementProgress
                {
                    achievementId = progressDto.achievementId,
                    currentCount = progressDto.currentCount,
                    isUnlocked = progressDto.isUnlocked,
                    unlockedAtTimestamp = progressDto.unlockedAt,
                });
            }
            tracker.LoadProgress(progressList);
        }

        isLoaded = true;
        DrainPendingEvents();
        AchievementsLoaded?.Invoke();
    }

    private void RegisterEvaluators()
    {
        tracker.RegisterEvaluator(new ThresholdCounterEvaluator());
        tracker.RegisterEvaluator(new OneTimeTriggerEvaluator());
        tracker.RegisterEvaluator(new CollectionCompletionEvaluator(slot =>
        {
            if (AccessoryRegistry.Instance == null) return false;
            var items = AccessoryRegistry.Instance.GetShopItemsForType(slot);
            return items.Count > 0 && items.TrueForAll(item => item.isOwned);
        }));
    }

    private void DrainPendingEvents()
    {
        while (pendingEvents.Count > 0)
        {
            var pendingEvent = pendingEvents.Dequeue();
            ReportEvent(pendingEvent.eventType, pendingEvent.context);
        }
    }

    private List<AchievementDefinition> MergeDefinitions(AchievementDefinitionResponse backendResponse)
    {
        var mergedDefinitions = new List<AchievementDefinition>();

        if (backendResponse?.achievements == null)
            return mergedDefinitions;

        foreach (var backendDefinition in backendResponse.achievements)
        {
            if (string.IsNullOrEmpty(backendDefinition.id))
                continue;

            var localDefinition = masterCatalog.GetDefinitionById(backendDefinition.id);
            if (localDefinition == null)
                continue;

            var merged = new AchievementDefinition
            {
                achievementId = backendDefinition.id,
                displayName = !string.IsNullOrEmpty(backendDefinition.name)
                    ? backendDefinition.name
                    : localDefinition.displayName,
                description = !string.IsNullOrEmpty(backendDefinition.description)
                    ? backendDefinition.description
                    : localDefinition.description,
                conditionType = localDefinition.conditionType,
                triggerEvent = localDefinition.triggerEvent,
                targetCount = backendDefinition.targetCount > 0
                    ? backendDefinition.targetCount
                    : localDefinition.targetCount,
                targetSlot = localDefinition.targetSlot,
                targetCurrency = !string.IsNullOrEmpty(backendDefinition.targetCurrency)
                    ? backendDefinition.targetCurrency
                    : localDefinition.targetCurrency,
                iconName = localDefinition.iconName,
            };

            mergedDefinitions.Add(merged);
        }

        return mergedDefinitions;
    }

    public void ReportEvent(GameEventType eventType, GameEventContext context)
    {
        if (!isLoaded)
        {
            pendingEvents.Enqueue(new PendingEvent { eventType = eventType, context = context });
            return;
        }

        var newlyUnlocked = tracker.ProcessEvent(eventType, context);

        foreach (string achievementId in newlyUnlocked)
        {
            dirtyProgressIds.Remove(achievementId);
            var progress = tracker.GetProgress(achievementId);
            string capturedId = achievementId;
            authority.RequestUnlock(
                capturedId,
                progress.currentCount,
                () => AchievementUnlocked?.Invoke(capturedId),
                (error) => Debug.LogWarning(
                    $"AchievementService: unlock rejected for '{capturedId}': {error}")
            );
        }

        foreach (string dirtyAchievementId in GetDirtyProgressFromEvent(eventType))
        {
            dirtyProgressIds.Add(dirtyAchievementId);
        }
    }

    private HashSet<string> GetDirtyProgressFromEvent(GameEventType eventType)
    {
        var dirtyAchievementIds = new HashSet<string>();
        foreach (var definition in tracker.GetAllDefinitions())
        {
            if (definition.triggerEvent == eventType)
            {
                var progress = tracker.GetProgress(definition.achievementId);
                if (progress != null && !progress.isUnlocked)
                    dirtyAchievementIds.Add(definition.achievementId);
            }
        }
        return dirtyAchievementIds;
    }

    private void Update()
    {
        if (!isLoaded || dirtyProgressIds.Count == 0) return;

        progressSyncTimer += Time.deltaTime;
        if (progressSyncTimer >= PROGRESS_SYNC_INTERVAL)
        {
            FlushDirtyProgress();
            progressSyncTimer = 0f;
        }
    }

    private void OnApplicationPause(bool isPaused)
    {
        if (isPaused && isLoaded)
            FlushDirtyProgress();
    }

    private void FlushDirtyProgress()
    {
        var snapshot = new List<string>(dirtyProgressIds);
        dirtyProgressIds.Clear();

        foreach (string achievementId in snapshot)
        {
            var progress = tracker.GetProgress(achievementId);
            if (progress != null)
                authority.ReportProgress(achievementId, progress.currentCount);
        }
    }

    public AchievementProgress GetProgress(string achievementId)
    {
        return tracker.GetProgress(achievementId);
    }

    public List<AchievementDefinition> GetAllDefinitions()
    {
        return tracker.GetAllDefinitions();
    }

    public bool IsLoaded()
    {
        return isLoaded;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            if (isLoaded)
                FlushDirtyProgress();
            Instance = null;
        }
    }
}
