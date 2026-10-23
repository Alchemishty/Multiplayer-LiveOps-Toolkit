# Enforcement: Serialization Safety

**Rule:** Every JSON deserialization must validate the result and throw on malformed data. Never assume response shape.

## Violations

### Unvalidated deserialization
```csharp
// VIOLATION — null result silently propagated
var accessories = JsonConvert.DeserializeObject<AccessoriesResponse>(responseBody);
foreach (var accessory in accessories.Items) // NullReferenceException on malformed JSON

// REQUIRED
var accessories = JsonConvert.DeserializeObject<AccessoriesResponse>(responseBody)
    ?? throw new InvalidOperationException(
        "Failed to deserialize AccessoriesResponse from API response");
if (accessories.Items == null)
    throw new InvalidOperationException(
        "AccessoriesResponse.Items was null — unexpected API response shape");
```

### Unvalidated API error responses
```csharp
// VIOLATION — assumes success without checking HTTP status
var response = await request.SendWebRequest();
var data = JsonConvert.DeserializeObject<WalletEntry>(response.downloadHandler.text);

// REQUIRED
var response = await request.SendWebRequest();
if (response.result != UnityWebRequest.Result.Success)
    throw new InvalidOperationException(
        $"API request failed: {response.responseCode} — {response.error}");
var data = JsonConvert.DeserializeObject<WalletEntry>(response.downloadHandler.text)
    ?? throw new InvalidOperationException(
        "Failed to deserialize WalletEntry from API response");
```

### AccessoryState JSON round-trip
```csharp
// VIOLATION — NetworkVariable JSON consumed without validation
private void OnAccessoryStateChanged(AccessoryState previous, AccessoryState current)
{
    var equipped = JsonConvert.DeserializeObject<List<string>>(current.JsonPayload);
    // equipped could be null

// REQUIRED
private void OnAccessoryStateChanged(AccessoryState previous, AccessoryState current)
{
    if (string.IsNullOrEmpty(current.JsonPayload))
        throw new InvalidOperationException(
            "AccessoryState.JsonPayload was null or empty on network sync");
    var equippedAccessoryIds = JsonConvert.DeserializeObject<List<string>>(current.JsonPayload)
        ?? throw new InvalidOperationException(
            $"Failed to deserialize equipped IDs from AccessoryState: {current.JsonPayload}");
```

## Scope

Applies to:
- All `JsonConvert.DeserializeObject` calls
- All `JsonUtility.FromJson` calls
- All manual JSON parsing
- All API response body reading
- All `AccessoryState` NetworkVariable value reads
