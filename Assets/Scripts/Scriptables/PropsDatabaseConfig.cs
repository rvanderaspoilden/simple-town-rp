using System.Collections.Generic;
using UnityEngine;

namespace Sim.Scriptables {
    [CreateAssetMenu(fileName = "Props Database", menuName = "Configurations/Database/Props")]
    public class PropsDatabaseConfig : ScriptableObject {
        [SerializeField] private List<PropsConfig> props;

        public List<PropsConfig> GetProps() {
            return this.props;
        }

        public PropsConfig GetPropsById(int id) {
            return props.Find(x => x.GetId() == id);
        }
    }
}