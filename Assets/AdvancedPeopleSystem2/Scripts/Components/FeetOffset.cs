using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AdvancedPeopleSystem
{
    public class FeetOffset : MonoBehaviour
    {

        private CharacterCustomization character;

        public List<FeetOffsetData> offsets = new List<FeetOffsetData>();

        private FeetOffsetData currentOffsetData = null;

        // Start is called before the first frame update
        void Start()
        {
            character = GetComponentInParent<CharacterCustomization>();
            if(character != null && character.Settings != null)
            {
                character.OnSelectNewElement += Character_OnSelectNewElement;
                character.OnClearElement += Character_OnClearElement;
            }
        }

        private void Character_OnClearElement(CharacterElementType type)
        {
            if(type == currentOffsetData?.type)
            {
                currentOffsetData = null;
            }
        }

        private void Character_OnSelectNewElement(CharacterElementType type, int index)
        {
            var data = offsets.Find(x => x.type == type && x.index == index && x.settingsName == character.selectedsettings.Name);
            if(data != null)
            {
                currentOffsetData = data;
            }else if(data == null && currentOffsetData != null && type == CharacterElementType.Shoes)
            {
                currentOffsetData = null;
            }
        }

        private void LateUpdate()
        {
           if(currentOffsetData != null)
            {
                character.originHip.localPosition = currentOffsetData.offset;
            }
        }

    }
    [Serializable]
    public class FeetOffsetData
    {
        public string settingsName;
        public CharacterElementType type;
        public int index;
        public Vector3 offset;
    }
}