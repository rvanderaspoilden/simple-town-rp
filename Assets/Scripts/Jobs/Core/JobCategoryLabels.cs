namespace Sim.Jobs {
    /// <summary>
    /// Display labels for JobCategory. Kept here so HUD / phone app / toast
    /// strings stay consistent. Replace with a localization key lookup once the
    /// project gets an i18n service.
    /// </summary>
    public static class JobCategoryLabels {
        public static string Display(JobCategory? category) {
            if (!category.HasValue) return "Chômeur";
            switch (category.Value) {
                case JobCategory.Delivery:  return "Livreur";
                case JobCategory.Cleaning:  return "Agent d'entretien";
                case JobCategory.Repair:    return "Réparateur";
                case JobCategory.Gardening: return "Jardinier";
                case JobCategory.Concierge: return "Concierge";
                case JobCategory.Music:     return "Musicien";
                default:                    return category.Value.ToString();
            }
        }

        public static string DisplayUpper(JobCategory? category) => Display(category).ToUpperInvariant();
    }
}
