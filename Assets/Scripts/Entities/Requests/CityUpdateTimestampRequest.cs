using System;

[Serializable]
public struct CityUpdateTimestampRequest {
    public string id;
    public long newTimestamp;
}