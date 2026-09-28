using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DeliveryDash.Game;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DeliveryDash.Editor.Tests
{
    // Loop tanpa batas (konsep pengguna 27 Sep): paket diambil di depan tempat paket, diantar ke rumah mana saja, lalu paket
    // muncul lagi. Tiap rumah harus punya tempat seukuran truk yang bebas dinding di dalam pemicunya (bisa dicapai dari jalan);
    // truk dipindah ke tempat itu dan antaran ke kelima rumah bergantian harus berhasil.
    // Batch: "Unity.exe -batchmode -executeMethod DeliveryDash.Editor.Tests.LoopChecks.RunBatch"
    // Hasil: Logs/loop-checks.txt (PASS/FAIL).
    public static class LoopChecks
    {
        private const string Flag = "DeliveryDash.GameLoopChecks";
        private const string ExitFlag = "DeliveryDash.GameLoopChecks.Exit";
        private static int step, house, checks;
        private static float waitUntil;
        private static readonly List<Vector2> parking = new List<Vector2>();

        [MenuItem("Delivery Dash/Uji Loop")]
        public static void Run()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Hentikan Play dahulu.");
            if (!OpenScene("Assets/Game/DeliveryDash.unity")) { Finish(false, "scene game tidak dapat dibuka"); return; }
            step = 0;
            house = 0;
            checks = 0;
            SessionState.SetBool(Flag, true);
            EditorApplication.EnterPlaymode();
        }

        // Buka scene tanpa membuang perubahan yang belum disimpan (batch: batal; editor: tanya dulu).
        private static bool OpenScene(string path)
        {
            UnityEngine.SceneManagement.Scene active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            bool dirty = false;
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                dirty |= UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty;
            if (!dirty && UnityEngine.SceneManagement.SceneManager.sceneCount == 1 && active.path == path) return true;
            if (dirty)
            {
                if (Application.isBatchMode)
                {
                    Debug.LogError($"Scene terbuka punya perubahan yang belum disimpan; {path} tidak dibuka supaya perubahan itu tidak hilang.");
                    return false;
                }
                if (!UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
            }
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path);
            return true;
        }

        public static void RunBatch()
        {
            SessionState.SetBool(ExitFlag, true);
            Run();
        }

        [InitializeOnLoadMethod]
        private static void Hook() => EditorApplication.update += Tick;

        private static void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            checks++;
        }

        private static void Wait(float seconds)
        {
            step++;
            waitUntil = Time.time + seconds;
        }

        private static void Tick()
        {
            if (!SessionState.GetBool(Flag, false) || !EditorApplication.isPlaying) return;
            if (Time.time < waitUntil) return;
            try
            {
                DeliveryLauncher launcher = UnityEngine.Object.FindFirstObjectByType<DeliveryLauncher>();
                // Scene 1.0 juga memasang skrip tabrakan di paket; truk = yang punya Rigidbody2D.
                DeliveryCollision truck = UnityEngine.Object.FindObjectsByType<DeliveryCollision>(FindObjectsSortMode.None).FirstOrDefault(item => item.GetComponent<Rigidbody2D>() != null);
                GameObject[] houses = GameObject.FindGameObjectsWithTag("Customer").OrderBy(item => item.name).ToArray();
                GameObject package = Resources.FindObjectsOfTypeAll<GameObject>().FirstOrDefault(item => item.CompareTag("Package") && item.scene.IsValid());
                switch (step)
                {
                    case 0:
                        VisualElement root = launcher == null ? null : launcher.GetComponent<UIDocument>()?.rootVisualElement;
                        if (root?.Q("btn:mulai") == null) return;
                        using (PointerUpEvent up = PointerUpEvent.GetPooled()) { up.target = root.Q("btn:mulai"); root.Q("btn:mulai").SendEvent(up); }
                        Check(launcher.Started && truck != null && package != null, "game dimulai dengan truk dan paket");
                        Check(houses.Length == 5, "lima rumah pelanggan, ada " + houses.Length);
                        Check(GameObject.Find("Package Yard") != null, "tempat paket ada di scene");
                        parking.Clear();
                        foreach (GameObject item in houses)
                        {
                            Vector2? spot = FreeSpot(item.GetComponent<Collider2D>().bounds, truck.GetComponent<BoxCollider2D>());
                            Check(spot.HasValue, "rumah " + item.name + " bisa dicapai truk (ada tempat bebas di pemicunya)");
                            parking.Add(spot.Value);
                        }
                        Wait(0.2f);
                        break;
                    case 1: // Ambil paket.
                        Check(package.activeSelf && !truck.HasPackage, $"antaran {house + 1}: paket tersedia di depan tempat paket");
                        Teleport(truck, package.transform.position);
                        Wait(0.8f);
                        break;
                    case 2: // Antar ke rumah berikutnya.
                        Check(truck.HasPackage && !package.activeSelf, $"antaran {house + 1}: paket terbawa truk");
                        Teleport(truck, parking[house]);
                        Wait(0.3f);
                        break;
                    case 3:
                        Check(!truck.HasPackage && truck.Deliveries == house + 1, $"antaran {house + 1} ke {houses[house].name} diterima (jumlah {truck.Deliveries})");
                        Teleport(truck, new Vector2(3.17f, -1f)); // menjauh dari paket
                        Wait(1.3f);
                        break;
                    case 4:
                        Check(package.activeSelf, $"antaran {house + 1}: paket baru muncul lagi");
                        house++;
                        if (house < houses.Length) { step = 1; break; }
                        Finish(true, null);
                        break;
                }
            }
            catch (Exception error)
            {
                Finish(false, error.Message);
            }
        }

        // Tempat seukuran kotak truk (tegak atau mendatar) di dalam pemicu rumah yang tidak menabrak collider padat.
        private static Vector2? FreeSpot(Bounds trigger, BoxCollider2D car)
        {
            Vector2 size = car.size * (Vector2)car.transform.lossyScale;
            var filter = new ContactFilter2D { useTriggers = false };
            var hits = new List<Collider2D>();
            for (float x = trigger.min.x; x <= trigger.max.x; x += 0.2f)
                for (float y = trigger.min.y; y <= trigger.max.y; y += 0.2f)
                    foreach (Vector2 box in new[] { size, new Vector2(size.y, size.x) })
                    {
                        hits.Clear();
                        Physics2D.OverlapBox(new Vector2(x, y), box, 0f, filter, hits);
                        if (hits.All(hit => hit.attachedRigidbody == car.attachedRigidbody)) return new Vector2(x, y);
                    }
            return null;
        }

        private static void Teleport(DeliveryCollision truck, Vector2 at)
        {
            var body = truck.GetComponent<Rigidbody2D>();
            body.linearVelocity = Vector2.zero;
            body.position = at;
            truck.transform.position = at;
        }

        private static void Finish(bool ok, string error)
        {
            SessionState.SetBool(Flag, false);
            string report = ok ? $"PASS: {checks} pemeriksaan loop (5 rumah)." : "FAIL: " + error;
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/loop-checks.txt", report);
            if (ok) Debug.Log("LOOP_CHECKS_OK: " + report);
            else Debug.LogError("LOOP_CHECKS_FAIL: " + report);
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
            if (SessionState.GetBool(ExitFlag, false))
            {
                SessionState.SetBool(ExitFlag, false);
                EditorApplication.Exit(ok ? 0 : 1);
            }
        }
    }
}
