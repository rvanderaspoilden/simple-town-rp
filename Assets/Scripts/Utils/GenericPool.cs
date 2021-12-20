using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class GenericPool<T> where T : MonoBehaviour {
    private List<T> activeElements;
    private List<T> availableElements;

    private Func<T> _actionOnCreate;
    private UnityAction<T> _actionOnGet;
    private UnityAction<T> _actionOnRelease;

    public GenericPool(Func<T> actionOnCreate, UnityAction<T> actionOnGet, UnityAction<T> actionOnRelease) {
        this.availableElements = new List<T>();
        this.activeElements = new List<T>();
        this._actionOnCreate = actionOnCreate;
        this._actionOnGet = actionOnGet;
        this._actionOnRelease = actionOnRelease;
    }

    public T Get() {
        T element = availableElements.Count > 0 ? availableElements[0] : this._actionOnCreate();
        
        this._actionOnGet?.Invoke(element);
        
        this.availableElements.Remove(element);
        this.activeElements.Add(element);
        
        return element;
    }

    public void Release(T element) {
        this._actionOnRelease?.Invoke(element);
        this.availableElements.Add(element);
        this.activeElements.Remove(element);
    }

    public void Dispose() {
        for (int i = 0; i < this.activeElements.Count; i++) {
            this.Release(this.activeElements[i]);
        }
    }
}