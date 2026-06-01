using System;

[Serializable]
public struct City {
    public string _id;
    public string name;
    public string mayor;
    public long last_timestamp;
    public long money;
    public int tax_by_month;
    public int unemployed_income;
    public int salary_period_seconds;
    public int rent_period_seconds;

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

    public int UnemployedIncome {
        get => unemployed_income;
        set => unemployed_income = value;
    }

    public int SalaryPeriodSeconds {
        get => salary_period_seconds;
        set => salary_period_seconds = value;
    }

    public int RentPeriodSeconds {
        get => rent_period_seconds;
        set => rent_period_seconds = value;
    }
}