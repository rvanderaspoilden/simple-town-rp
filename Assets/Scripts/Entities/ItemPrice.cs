using System;

[Serializable]
public struct ItemPrice {
    public ItemConfig item;
    public int price;

    public string DisplayWithPrice() {
        return $"{this.item.Label}  ({price}$)";
    }
}
