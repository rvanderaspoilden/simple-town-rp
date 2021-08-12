using System;

[Serializable]
public struct City {
    public string _id;
    public string name;
    public string mayor;
    public long last_timestamp;
    public long money;
    public int tax_by_month;

    public string ID {
        get => _id;
        set => _id = value;
    }

    public string Name {
        get => name;
        set => name = value;
    }

    public string Mayor {
        get => mayor;
        set => mayor = value;
    }

    public long LastTimestamp {
        get => last_timestamp;
        set => last_timestamp = value;
    }

    public long Money {
        get => money;
        set => money = value;
    }

    public int TaxByMonth {
        get => tax_by_month;
        set => tax_by_month = value;
    }
}