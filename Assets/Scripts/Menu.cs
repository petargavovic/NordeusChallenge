using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class Menu : MonoBehaviour
{
    public Animator camera;

    public TextMeshProUGUI accuracyText;
    public IslandGenerator islandGenerator;

    float accuracy;

    void Start()
    {
        if (PlayerPrefs.GetInt("guesses") == 0)
            accuracyText.enabled = false;
        else
        {
            int correct = PlayerPrefs.GetInt("correct");
            if (correct != 0)
                accuracy = (float)PlayerPrefs.GetInt("correct") / PlayerPrefs.GetInt("guesses") * 100;
            else
                accuracy = 0;
            accuracyText.text = "accuracy: " + accuracy.ToString("#.##") + "%";
        }
    }

    public void VariableMode()
    {
        islandGenerator.variableMode = true;
        islandGenerator.enabled = true;
        camera.Play("camRotate");
    }

    public void InvariableMode()
    {
        islandGenerator.variableMode = false;
        islandGenerator.enabled = true;
        camera.Play("camRotate");
    }

    public void Quit()
    {
        Application.Quit();
    }
}
