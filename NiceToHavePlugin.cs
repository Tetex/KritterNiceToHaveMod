using BepInEx;
using BepInEx.Logging;
using BepInEx.Configuration;
using UnityEngine;
using HarmonyLib;
using LJF.UI;
using System.Reflection;
using System;
using LJF.Game.Entities;
using System.IO;
using System.Xml.Linq;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace KritterNiceToHavePlugin;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class NiceToHavePlugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;
    static Texture2D crosshairTexture = null;
    static Texture2D menuCursor = null;
    static bool isInGame = false;

    //Config
    public static ConfigEntry<bool> EnableCrosshair;
    public static ConfigEntry<Color> CrosshairColor;
    public static ConfigEntry<bool> LockCursorInWindow;
    public static ConfigEntry<bool> PopupBeforeLeave;
    public static ConfigEntry<bool> EnableAutoLoot;

    private void Awake()
    {
        // Plugin startup logic
        Logger = base.Logger;
        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");

        EnableCrosshair = Config.Bind<bool>("Cursor", "EnableCrosshair", true, "Enable ingame crosshair");
        CrosshairColor = Config.Bind<Color>("Cursor", "CrosshairColor", Color.darkBlue, "Color of the ingame crosshair");
        LockCursorInWindow = Config.Bind<bool>("Cursor", "LockCursorInWindow", true, "Lock cursor inside the window");
        PopupBeforeLeave = Config.Bind<bool>("Ingame Menu", "PopupBeforeLeave", true, "Show a confirmation popup before leaving");
        EnableAutoLoot = Config.Bind<bool>("Loot", "EnableAutoLoot", true, "Auto loot items (except potions and probably boss items etc...)");

        SceneManager.sceneLoaded += OnSceneLoaded;

        Harmony harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        harmony.PatchAll();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (crosshairTexture == null || menuCursor == null)
        {
            Texture2D[] textures = Resources.FindObjectsOfTypeAll<Texture2D>();

            foreach (var tex in textures)
            {
                if (tex.name == "perk_role_tracker" && crosshairTexture == null)
                {
                    crosshairTexture = ToReadableCopy(tex);
                    SetTextureColor(ref crosshairTexture, CrosshairColor.Value);
                }
                else if (tex.name == "cursor" && menuCursor == null)
                {
                    menuCursor = ToReadableCopy(tex);
                }
            }
        }

        isInGame = scene.name.Contains("Game");

        if (isInGame && crosshairTexture && EnableCrosshair.Value)
        {
            Vector2 texCenter = new Vector2(crosshairTexture.width / 2, crosshairTexture.height / 2);
            Cursor.SetCursor(crosshairTexture, texCenter, CursorMode.Auto);
        }
        else if (menuCursor)
        {
            Vector2 texTopLeft = new Vector2(0, 0);
            Cursor.SetCursor(menuCursor, texTopLeft, CursorMode.Auto);
        }
    }

    void Update()
    {
        if (NiceToHavePlugin.isInGame && NiceToHavePlugin.LockCursorInWindow.Value)
        {
            Cursor.lockState = CursorLockMode.Confined;
        }
    }

    public static Texture2D ToReadableCopy(Texture2D tex)
    {
        RenderTexture tmpTex = RenderTexture.GetTemporary(
            tex.width,
            tex.height,
            0,
            RenderTextureFormat.Default,
            RenderTextureReadWrite.Linear);

        Graphics.Blit(tex, tmpTex);

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = tmpTex;

        Texture2D readableTex = new Texture2D(tex.width, tex.height);

        readableTex.ReadPixels(new Rect(0, 0, tmpTex.width, tmpTex.height), 0, 0);
        readableTex.Apply();

        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(tmpTex);

        return readableTex;
    }

    public static void SetTextureColor(ref Texture2D tex, Color color)
    {
        Color[] pixels = tex.GetPixels();

        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i].r = color.r;
            pixels[i].g = color.g;
            pixels[i].b = color.b;
            //pixels[i].a = color.a;
        }

        tex.SetPixels(pixels);
        tex.Apply();
    }

    public static void FakeInput(InputDevice device, string controlName, float inputVal)
    {
        if (device == null)
        {
            return;
        }

        var control = device[controlName];
        if (control == null) 
        { 
            return; 
        }

        using (StateEvent.From(device, out var stateEvent))
        {
            control.WriteValueIntoEvent<float>(inputVal, stateEvent);

            InputSystem.QueueEvent(stateEvent);
        }
    }
}

[HarmonyPatch(typeof(MenuController))]
public static class MenuControllerPatch
{
    static GameObject validationPopup;
    static GameObject validationPopupPrefab;

    [HarmonyPatch("Open")]
    [HarmonyPostfix]
    public static void PostOpenFix(ref MenuController __instance)
    {
        if (!NiceToHavePlugin.PopupBeforeLeave.Value)
        {
            return;
        }

        if (validationPopupPrefab == null)
        {
            GameObject[] gameObjects = Resources.FindObjectsOfTypeAll<GameObject>();

            foreach (var gameObject in gameObjects)
            {
                if (gameObject.name == "ValidationPopup")
                {
                    NiceToHavePlugin.Logger.LogInfo("Asset ValidationPopup found!");
                    validationPopupPrefab = gameObject;
                    break;
                }
            }
        }

        if (validationPopup != null)
        {
            Transform panelTransform = __instance.transform.Find("Panel");
            if (panelTransform != null)
            {
                GameObject panel = panelTransform.gameObject;
                panel.SetActive(false);
            }
        }
    }


    static bool shouldLeave = false;
    static MenuController menuControllerInstance = null;
    [HarmonyPatch("QuitGame")]
    [HarmonyPrefix]
    public static bool PreQuitGameFix(ref MenuController __instance)
    {
        if (!NiceToHavePlugin.PopupBeforeLeave.Value)
        {
            return true;
        }

        if (!shouldLeave)
        {
            menuControllerInstance = __instance;

            validationPopup = GameObject.Instantiate(validationPopupPrefab);
            validationPopup.transform.SetParent(__instance.transform, false);

            ValidationPopupUI validationPopupUI = validationPopup.GetComponent<ValidationPopupUI>();
            validationPopupUI.Init("Leave", "Are you sure you want to leave?");

            Transform panelTransform = menuControllerInstance.transform.Find("Panel");
            if (panelTransform != null)
            {
                GameObject panel = panelTransform.gameObject;
                panel.SetActive(false);
            }

            validationPopupUI.OnCancelAction = delegate
            {
                GameObject.Destroy(validationPopup);
                validationPopup = null;
                Transform panelTransform = menuControllerInstance.transform.Find("Panel");
                if (panelTransform != null)
                {
                    GameObject panel = panelTransform.gameObject;
                    panel.SetActive(true);
                }
            };

            validationPopupUI.OnValidateAction = delegate
            {
                GameObject.Destroy(validationPopup);
                validationPopup = null;
                shouldLeave = true;
                menuControllerInstance.QuitGame();
                menuControllerInstance = null;
            };

            return false;
        }

        shouldLeave = false;
        return true;
    }
}

[HarmonyPatch(typeof(PlayerActionsController))]
public static class PlayerActionsControllerPatch
{
    private static bool _isPressingFakePickupKey = false;
    [HarmonyPatch("Update")]
    [HarmonyPostfix]
    public static void PostUpdateFix(ref PlayerActionsController __instance)
    {
        if (NiceToHavePlugin.EnableAutoLoot.Value)
        {
            if (_isPressingFakePickupKey)
            {
                NiceToHavePlugin.FakeInput(Keyboard.current, "e", 0);
                _isPressingFakePickupKey = false;
            }

            ActionDetector actionDetector = __instance.GetComponent<ActionDetector>();
            ActivableBehaviour currentActivable = actionDetector.GetCurrentActivable();
            if (currentActivable != null)
            {
                // TODO: see if we want to take "Potion" (maybe when life is low)
                if (currentActivable.name.Contains("PowerUp") || currentActivable.name.Contains("Stat"))
                {
                    NiceToHavePlugin.FakeInput(Keyboard.current, "e", 1);

                    _isPressingFakePickupKey = true;
                }
            }
        }
    }
}