using UnityEngine;
using UnityEngine.UI;

public class SettingsManager : MonoBehaviour
{
    [Header("Panel")]
    public GameObject settingsPanel;
    public GameObject buttonGroup;

    [Header("Sliders")]
    public Slider volumeSlider;
    public Slider sensitivitySlider;

    void Start()
    {
        volumeSlider.value = PlayerPrefs.GetFloat("Volume", 0.75f);
        sensitivitySlider.value = PlayerPrefs.GetFloat("Sensitivity", 0.5f);
    }

    public void OpenSettings()
    {
        settingsPanel.SetActive(true);
        buttonGroup.SetActive(false);
    }

    public void CloseSettings()
    {
        settingsPanel.SetActive(false);
        buttonGroup.SetActive(true);
    }

    public void OnVolumeChanged(float value)
    {
        AudioListener.volume = value;
        PlayerPrefs.SetFloat("Volume", value);
    }

    public void OnSensitivityChanged(float value)
    {
        PlayerPrefs.SetFloat("Sensitivity", value);
    }
}