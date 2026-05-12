using System.Collections.Generic;
using System.Linq;
using Sim.Building;

namespace Sim.Utils {
    public static class SaveUtils {
        /// <summary>
        /// Project a cover-settings dictionary onto the wire format consumed by
        /// the C2S apply messages and the new /covers endpoint.
        /// </summary>
        public static CoverData[] CreateCoverDatas(Dictionary<int, CoverSettings> settings) {
            return settings.Select(pair => new CoverData {
                idx = pair.Key,
                additionalColor = pair.Value.GetColor(),
                paintConfigId = pair.Value.paintConfigId
            }).ToArray();
        }
    }
}
