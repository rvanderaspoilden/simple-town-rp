namespace Sim.Audio {
    /// <summary>
    /// Identifiant type-safe d'un effet sonore. Mappé vers un <see cref="SfxEntry"/> dans le
    /// <see cref="SfxCatalog"/>. Ajouter une valeur ici + une entrée dans le catalogue suffit ;
    /// aucun chemin string, aucune ré-écriture de code. Ne pas réordonner (les entrées du
    /// catalogue référencent l'enum par valeur, pas par index).
    /// </summary>
    public enum SfxId {
        None = 0,

        // ── Locomotion (3D) ──
        FootstepDefault,
        FootstepWood,
        FootstepTile,
        FootstepCarpet,
        FootstepOutdoor,

        // ── Joueur (3D) ──
        Eat,
        Drink,
        ItemPickup,
        ItemDrop,
        ActionFail,

        // ── Inventaire / conteneurs ──
        InventoryOpen,
        InventoryClose,
        ItemMove,
        ItemSwap,
        ContainerOpen,
        ContainerClose,
        PropPack,
        PropUnpack,

        // ── Props ──
        PropBuild,
        PropDestroy,
        TrashThrow,
        DoorOpen,
        DoorClose,
        DoorLock,
        DoorUnlock,
        DoorRing,
        DispenserUse,
        LightSwitch,

        // ── Économie ──
        ShopBuy,
        Sell,
        MoneyReceive,
        RentPay,

        // ── Social / téléphone ──
        Handshake,
        SmsReceive,
        SmsSend,
        CallRing,
        Notification,

        // ── Missions ──
        MissionStep,
        MissionComplete,
        Reward,

        // ── UI ──
        UiHover,
        UiClick,
        UiBack,
        UiPhoneLock,
        UiPhoneUnlock,
        UiToastSuccess,
        UiToastError,
        UiBubble,
    }
}
