using System.Collections;
using Mirror;
using UnityEngine;

public class NetworkEntity : NetworkBehaviour
{
    [SyncVar]
    private uint parentId;
    
    public override void OnStartClient() {
        base.OnStartClient();
            
        if (!isActiveAndEnabled || parentId == 0 || !isClientOnly) return;

        this.StartCoroutine(this.AssignParentCoroutine());
    }
    
    private IEnumerator AssignParentCoroutine() {
        int retryCounter = 0;

        while (retryCounter < 10 && !NetworkIdentity.spawned.ContainsKey(this.parentId)) {
            retryCounter++;
            yield return new WaitForSeconds(.3f);
        }

        this.AssignParent();
    }

    protected virtual void AssignParent() {
        Transform curTransform = this.transform;
        Vector3 position = curTransform.position;
        Quaternion rotation = curTransform.rotation;

        if (NetworkIdentity.spawned.ContainsKey(this.parentId)) {
            curTransform.SetParent(NetworkIdentity.spawned[this.parentId].transform);
            curTransform.localPosition = position;
            curTransform.localRotation = rotation;
        } else {
            Debug.LogError($"Parent identity not found for {this.name}");
        }
    }
    
    public uint ParentId {
        get => parentId;
        set => parentId = value;
    }
}
