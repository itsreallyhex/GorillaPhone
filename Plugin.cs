using BepInEx;
using BepInEx.Configuration;
using GorillaLocomotion;
using GorillaPhone.Diag;
using GorillaPhone.Phone;
using GorillaPhone.Util;
using UnityEngine;

namespace GorillaPhone
{
    [BepInPlugin(PluginInfo.GUID, PluginInfo.NAME, PluginInfo.Version)]
    public class Plugin : BaseUnityPlugin
    {
        // Frames to wait after the player and controllers exist before building anything.
        const int SettleFrames = 300;

        PhoneConfig phoneCfg;
        ConfigEntry<bool> runPhase0;
        ConfigWatcher watcher;
        PhoneObject phone;

        bool spawned;
        bool phase0Started;
        int readyFrames;
        float nextSpawnTry;

        void Awake()
        {
            phoneCfg = new PhoneConfig(Config);
            runPhase0 = Config.Bind("Diagnostics", "RunPhase0", false,
                "Run the one-time phase 0 diagnostics after loading in (about 20 seconds, stand still). Report: BepInEx\\GorillaPhone-phase0.txt. Resets to false after it runs.");

            watcher = new ConfigWatcher(Config.ConfigFilePath);

            // Only set a flag here; building anything inside this callback is not safe (kit learning).
            GorillaTagger.OnPlayerSpawned(() => spawned = true);
            Logger.LogInfo(PluginInfo.NAME + " " + PluginInfo.Version + " loaded");
        }

        void Update()
        {
            // Reload the config only when the file changed on disk (a background watcher sets the flag).
            if (watcher != null && watcher.TryConsume())
            {
                Config.Reload();
                Logger.LogInfo("config reloaded");
            }

            if (!spawned || !GorillaTagger.hasInstance || !GTPlayer.hasInstance || ControllerInputPoller.instance == null) return;
            if (readyFrames < SettleFrames) { readyFrames++; return; }

            if (runPhase0.Value && !phase0Started)
            {
                phase0Started = true;
                runPhase0.Value = false;
                Logger.LogInfo("phase 0 diagnostics starting; stand still for about 20 seconds");
                StartCoroutine(new PhaseZeroDiag(Logger).Run());
            }

            // Unity's null check is true again if the game destroyed the phone (for example with a rig).
            if (phone == null && phoneCfg.Enabled.Value && Time.time >= nextSpawnTry)
            {
                nextSpawnTry = Time.time + 3f;
                phone = PhoneObject.Create(phoneCfg, Logger);
            }
        }

        void OnDestroy()
        {
            if (watcher != null) watcher.Dispose();
            if (phone != null) Destroy(phone.gameObject);
        }
    }
}
