using System.Collections;
using Newtonsoft.Json;
using Sim;
using Sim.Entities.Persistence;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Server-side inventory anchor for a character. Holds the place UUIDs of
/// the character's pockets and two equipment slots (hand_left, hand_right).
/// Persistence lives in DB places — PlayerInventory just caches the UUIDs.
///
/// Created idempotently on character connect via EnsurePlaces. The component
/// is attached to the player GameObject on the server side; clients don't
/// need it (the gameplay state they care about flows through ServerItemManager
/// + S2C messages).
/// </summary>
public class PlayerInventory : MonoBehaviour
{
    /// <summary>Pocket place — type='inventory', owner=characterId, capacity TBD.</summary>
    public string PocketPlaceId    { get; private set; }

    /// <summary>Left hand slot — type='equipment', capacity 1.</summary>
    public string HandLeftPlaceId  { get; private set; }

    /// <summary>Right hand slot — type='equipment', capacity 1.</summary>
    public string HandRightPlaceId { get; private set; }

    /// <summary>True iff all three place UUIDs have been resolved.</summary>
    public bool   PlacesReady => !string.IsNullOrEmpty(PocketPlaceId)
                              && !string.IsNullOrEmpty(HandLeftPlaceId)
                              && !string.IsNullOrEmpty(HandRightPlaceId);

    /// <summary>Returns the DB place UUID corresponding to a hand slot, or null
    /// if EnsurePlaces hasn't completed yet.</summary>
    public string HandPlaceFor(HandType hand) =>
        hand == HandType.Left ? HandLeftPlaceId : HandRightPlaceId;

    /// <summary>
    /// Idempotently creates (or fetches) the three places attached to this
    /// character. POST /places returns the existing place when the place_key
    /// already exists, so calling this on every connect is safe and cheap.
    /// </summary>
    public IEnumerator EnsurePlaces(string characterId)
    {
        if (string.IsNullOrEmpty(characterId)) {
            Debug.LogWarning("[PlayerInventory] EnsurePlaces called with empty characterId");
            yield break;
        }

        yield return EnsureOne($"pocket:{characterId}",     "inventory",  characterId, id => PocketPlaceId    = id);
        yield return EnsureOne($"hand_left:{characterId}",  "equipment",  characterId, id => HandLeftPlaceId  = id);
        yield return EnsureOne($"hand_right:{characterId}", "equipment",  characterId, id => HandRightPlaceId = id);
    }

    private IEnumerator EnsureOne(string placeKey, string type, string characterId, System.Action<string> onResolved)
    {
        CreatePlaceBody body = new CreatePlaceBody {
            placeKey = placeKey,
            type     = type,
            ownerId  = characterId,
            tenantId = characterId,
        };
        UnityWebRequest req = ApiManager.Instance.CreatePlaceRequest(body);
        yield return req.SendWebRequest();

        if (req.responseCode < 200 || req.responseCode >= 300) {
            Debug.LogWarning($"[PlayerInventory] EnsurePlaces({placeKey}) failed code={req.responseCode} body={req.downloadHandler?.text}");
            yield break;
        }

        try {
            PlaceJson place = JsonConvert.DeserializeObject<PlaceJson>(req.downloadHandler.text);
            if (place != null && !string.IsNullOrEmpty(place.Id)) onResolved(place.Id);
            else Debug.LogWarning($"[PlayerInventory] EnsurePlaces({placeKey}): empty place response");
        } catch (System.Exception e) {
            Debug.LogWarning($"[PlayerInventory] EnsurePlaces({placeKey}): parse error {e.Message}");
        }
    }
}
