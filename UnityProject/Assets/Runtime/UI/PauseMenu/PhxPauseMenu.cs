using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class PhxPauseMenu : PhxMenuInterface
{
    PhxGameMatch MTC => PhxGameRuntime.GetMatch();

    [Header("References")]
    public Button BtnContinue;
    public Button BtnFreeCam;
    public Button BtnNextMap;
    public Button BtnBackToMainMenu;
    public Button BtnQuit;


    public override void Clear()
    {

    }

    void Start()
    {
        Debug.Assert(BtnContinue       != null);
        Debug.Assert(BtnFreeCam        != null);
        Debug.Assert(BtnNextMap        != null);
        Debug.Assert(BtnBackToMainMenu != null);
        Debug.Assert(BtnQuit           != null);

        BtnFreeCam.GetComponentInChildren<Text>().text = MTC.PlayerST != PhxGameMatch.PlayerState.FreeCam ? "Free Cam" : "Character Selection";

        BtnContinue.onClick.AddListener(Continue);
        BtnFreeCam.onClick.AddListener(FreeCam);
        BtnNextMap.onClick.AddListener(NextMap);
        BtnBackToMainMenu.onClick.AddListener(BackToMainMenu);
        BtnQuit.onClick.AddListener(Quit);
    }

    void Continue()
    {
        PhxGameRuntime.Instance.RemoveMenu();
    }

    void FreeCam()
    {
        if (MTC.PlayerST == PhxGameMatch.PlayerState.CharacterSelection)
        {
            MTC.SetPlayerState(PhxGameMatch.PlayerState.FreeCam);
        }
        else if (MTC.PlayerST == PhxGameMatch.PlayerState.FreeCam)
        {
            MTC.SetPlayerState(PhxGameMatch.PlayerState.CharacterSelection);
        }
    }

    void NextMap()
    {
        PhxGameRuntime.Instance.NextMap();
    }

    void BackToMainMenu()
    {
        PhxGameRuntime.Instance.EnterMainMenu();
    }

    void Quit()
    {
        Application.Quit();
    }
}