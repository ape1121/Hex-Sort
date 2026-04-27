using Ape.Core;
using UnityEngine;

namespace Ape.Profile
{
    public sealed class ProfileManager : IManager
    {
        private const string SaveKey = "HexSort.SaveData";

        private SaveData _currentData;

        public SaveData CurrentData => _currentData;

        public void Initialize()
        {
            Load();
        }

        public SaveData Load()
        {
            if (!PlayerPrefs.HasKey(SaveKey))
            {
                _currentData = default;
                return _currentData;
            }

            _currentData = JsonUtility.FromJson<SaveData>(PlayerPrefs.GetString(SaveKey));
            return _currentData;
        }

        public void Save()
        {
            string json = JsonUtility.ToJson(_currentData);
            PlayerPrefs.SetString(SaveKey, json);
            PlayerPrefs.Save();
        }

        public void SetData(SaveData saveData, bool saveImmediately = true)
        {
            _currentData = saveData;

            if (saveImmediately)
                Save();
        }

        public void Reset()
        {
            _currentData = default;
            Save();
        }
    }
}
