// SD2 Minimal Fix 2.0 — BepInEx 5 plugin for Soundodger 2.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace SD2MinimalFix
{
	[BepInPlugin("sd2.minimalfix", "SD2 Minimal Fix", "2.0.0")]
	public class Plugin : BaseUnityPlugin
	{
		public static BepInEx.Logging.ManualLogSource Log;

		void Awake()
		{
			Log = Logger;
			try { new Harmony("sd2.minimalfix").PatchAll(); }   // применяет каждый класс ниже, отмеченный [HarmonyPatch]
			catch (Exception e) { Log.LogError("Patching failed, some fixes are missing: " + e); }

			var label = new GameObject("SD2MinimalFix_Label");
			label.hideFlags = HideFlags.HideAndDontSave;
			DontDestroyOnLoad(label);
			label.AddComponent<ModdedLabel>();
			Log.LogInfo("SD2 Minimal Fix 2.0 loaded");
		}
	}

	// Надпись MODDED
	public class ModdedLabel : MonoBehaviour
	{
		private GUIStyle _style;
		private float _alpha = 1f;
        private void Update(){
			bool inLevel = Clock.Main != null && Clock.Main.mode == MainGame.mainModes.game;
			_alpha = inLevel ? 0f : 1f;
		}
		void OnGUI()
		{
			if (_style == null) _style = new GUIStyle(GUI.skin.label) { fontSize = 13 };

			Color oldColor = OnGUI.color;

			// Тень
			GUI.color = new Color(0f, 0f, 0f, _alpha * 0.8f);
            GUI.Label(new Rect(11f, Screen.height - 25f, 200f, 25f), "MODDED", _style);
            
            // Текст
            GUI.color = new Color(1f, 0.85f, 0.2f, _alpha);
            GUI.Label(new Rect(10f, Screen.height - 26f, 200f, 25f), "MODDED", _style);
            
            GUI.color = oldColor;
		}
	}

	// Доступ к приватным полям игры
	public static class GameFields
	{
		private static readonly FieldInfo StopwatchStart = AccessTools.Field(typeof(MainGame), "stopwatchStart");
		private static readonly FieldInfo StopwatchAudioStart = AccessTools.Field(typeof(MainGame), "stopwatchAudioStart");
		private static readonly FieldInfo PlaybackFactor = AccessTools.Field(typeof(MainGame), "playbackFactor");
		private static readonly FieldInfo LastAbsSpin = AccessTools.Field(typeof(MainGame), "lastAbsSpin");

		public static float GetPlaybackFactor(MainGame m) => (float)PlaybackFactor.GetValue(m);
		public static bool IsStopwatchRunning(MainGame m) => (float)StopwatchStart.GetValue(m) != -1f;
		public static float GetCheckpointTime(MainGame m) => ((Vector2)LastAbsSpin.GetValue(m)).x;

		public static float GetStopwatchTime(MainGame m)
		{
			float start = (float)StopwatchStart.GetValue(m);
			float audioStart = (float)StopwatchAudioStart.GetValue(m);
			return audioStart + (Time.time - start) * GetPlaybackFactor(m);
		}

		public static void SetStopwatchTime(MainGame m, float time)
        {
            float audioStart = (float)StopwatchAudioStart.GetValue(m);
            StopwatchAudioStart.SetValue(m, audioStart + time - GetStopwatchTime(m));
        }
    }

	// ============================================================================================ 1. Clock
	public static class Clock
	{
		private const double Step = 1024.0 / 48000.0;   // 21.333 мс (стандартный аудиобуфер)
        private const double MaxDriftRate = 0.005;      // макс. скорость коррекции (0.5%)
        private const double Deadband = 0.002;          // не корректируем, если ошибка < 2 мс
        private const double JumpLimit = 0.15;          // если музыка ушла дальше, это был seek

		public static MainGame Main;
        public static bool IsReady;
        public static double T; // Текущее время в секундах

        private static float _lastAudio = -1f;
        private static double _audioStep = Step;
        private static readonly List<double> _errors = new List<double>();
        private static bool _waiting;
        private static float _seekTarget = -1f;

		public static bool IsActive(MainGame m)
        {
            return m != null && !m.inEditor && m.audioSource != null && m.audioSource.clip != null;
        }

		private static bool IsPlaying(MainGame m)
        {
            return m.audioSource.isPlaying && 
                (m.mode == MainGame.mainModes.game || m.mode == MainGame.mainModes.pausing || m.mode == MainGame.mainModes.unpausing);
        }

		public static void Reset()
        {
            IsReady = false; _lastAudio = -1f; _errors.Clear(); _waiting = false; _seekTarget = -1f;
        }

		public static void Seek(AudioSource src)
        {
            T = src.time; IsReady = true; _lastAudio = src.time; _errors.Clear();
            _waiting = true; _seekTarget = src.time;
        }

		public static void Tick(MainGame m)
		{
			AudioSource src = m.audioSource;
			float audio = src.time;

			if (!IsPlaying(m)) 
            { 
                T = audio; IsReady = true; _lastAudio = audio; _errors.Clear(); _waiting = false; _seekTarget = -1f; 
                return; 
            }

			if (!IsReady) Seek(src);

			float dt = Time.unscaledDeltaTime;
            float pitch = src.pitch;

			if (_waiting)
            {
                if (audio != _lastAudio && audio > _seekTarget + 0.0001f)
                {
                    T = Math.Max(_seekTarget, audio - _audioStep * pitch * 0.5 + dt * pitch * 0.5);
                    _waiting = false; 
                    _errors.Clear();
                }
                else 
                {
                    T = _seekTarget;
                }
                _lastAudio = audio;
                return;
            }

			double before = T;
			T += dt * pitch;

			// Если музыка изменилась, запоминаем насколько далеко она от clock
			if (audio != _lastAudio)
            {
                float step = audio - _lastAudio;
                _lastAudio = audio;
                
                if (Math.Abs(audio - T) > JumpLimit) 
                {
                    Seek(src);
                }
                else if (step > 0f && step < 0.1f)
                {
                    if (pitch > 0.5f) 
                    {
                        _audioStep += (step / pitch - _audioStep) * 0.05;
                    }
                    _errors.Add(audio - (T - dt * pitch * 0.5));
                    if (_errors.Count > 16) 
                    {
                        _errors.RemoveAt(0);
                    }
                }
            }

			// коррекция дрифта
			if (!_waiting && _errors.Count >= 8 && pitch > 0.05f)
            {
                double error = _errors.Average() - _audioStep * pitch * 0.5;
                if (Math.Abs(error) > Deadband)
                {
                    double max = dt * pitch * MaxDriftRate;
                    double fix = Math.Max(-max, Math.Min(max, error));
                    T += fix;
                    for (int i = 0; i < _errors.Count; i++) 
                    {
                        _errors[i] -= fix;
                    }
                }
            }
            if (!_waiting && T < before) 
            {
                T = before;
            }
		}

		// заменяет audioSource.time, где MainGame.Update записывает audioTime
		public static float AudioTimeFor(AudioSource src)
		{
			float audio = src.time;
            if (!IsActive(Main) || !src.isPlaying) return audio;
            
            if (!IsReady || (!_waiting && Math.Abs(audio - T) > JumpLimit)) 
            { 
                Seek(src); 
                return (float)T; 
            }
            if (_waiting) return (float)T;

			// часы фиксированными шагами считают от начала песни (T это середина шага)
			double q = Step * Math.Max(0.05, GameFields.GetPlaybackFactor(Main));
            double stepped = Math.Floor((T + q * 0.5) / q + 1e-9) * q;
			
			if (_seekTarget >= 0f && stepped < _seekTarget && stepped > _seekTarget - q * 1.5) 
            {
                stepped = _seekTarget;
            }
            
            return (float)stepped;
		}
	}

	[HarmonyPatch(typeof(MainGame), "Start")]
	static class StartPatch
	{
		static void Postfix(MainGame __instance)
		{
			Clock.Main = __instance;
			Clock.Reset();
			RingPatch.ResetLevel();
		}
	}

	[HarmonyPatch(typeof(MainGame), "beginPractice")]
	static class PracticePatch
	{
		static void Prefix(MainGame __instance)
		{
			RingPatch.ResetSeek();
            if (Clock.IsActive(__instance)) 
            {
                Clock.Seek(__instance.audioSource);
            }
		}
	}

	[HarmonyPatch(typeof(MainGame), "Update")]
	static class UpdatePatch
	{
		static void Prefix(MainGame __instance)
		{
			static void Prefix(MainGame __instance)
        {
            try 
            { 
                if (Clock.IsActive(__instance)) 
                {
                    Clock.Tick(__instance); 
                }
            }
            catch (Exception e) 
            { 
                Plugin.Log.LogError("Clock Tick error: " + e.Message); 
            }
        }
		}

		// audioTime = audioSource.time  ->  audioTime = Clock.AudioTimeFor(audioSource)
		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var getTime = AccessTools.PropertyGetter(typeof(AudioSource), "time");
			var audioTimeField = AccessTools.Field(typeof(MainGame), "audioTime");
            var clockTimeMethod = AccessTools.Method(typeof(Clock), "AudioTimeFor");

			return new CodeMatcher(instructions)
                .MatchForward(false, 
                    new CodeMatch(OpCodes.Call, getTime),
                    new CodeMatch(OpCodes.Stfld, audioTimeField))
                .Repeat(matcher => matcher.SetOpcodeAndOperand(OpCodes.Call, clockTimeMethod))
                .InstructionEnumeration();
		}
	}

	// ============================================================================================ 2 + 3. Ring
	[HarmonyPatch(typeof(MainGame), "absoluteSpin")]
	static class RingPatch
	{
		private const float SpinRate = 1.0016667f; // 601/600

        private static float _ringFloor = -1f;
        private static bool _sortChecked;

		public static void ResetLevel() { _ringFloor = -1f; _sortChecked = false; }
        public static void ResetSeek() { _ringFloor = -1f; }

		static void Prefix(MainGame __instance, bool fromStart)
		{
			MainGame m = __instance;
			if (m.inEditor) return;

			try
			{
				if (!_sortChecked)
                {
                    _sortChecked = true;
                    SortArenaEvents();
                }
				if (!Clock.IsActive(m) || !Clock.IsReady || fromStart || !GameFields.IsStopwatchRunning(m)) 
                    return;
                if (LevelData.warm && !LevelData.testing) 
                    return; 

				float target = (float)Clock.T;
				if (target < _ringFloor - 0.2f) _ringFloor = -1f;
				if (target < _ringFloor) target = _ringFloor;

				float ringTime = target;
                float gameTime = m.audioTime;

				if (gameTime > ringTime && LevelData.arenaAngles.Any(e => e.time > ringTime && e.time <= gameTime))
                {
                    target = gameTime + 0.0001f;
                }

				_ringFloor = target;
				GameFields.SetStopwatchTime(m, Math.Max(target, GameFields.GetCheckpointTime(m)));
			}
			catch (Exception e) 
            { 
                Plugin.Log.LogError("Ring Prefix error: " + e.Message); 
            }
		}

		// Код обработки вращения выбирает событие, связанное с углом арены, проходя по списку в обратном порядке. Для этого список должен быть отсортирован по времени
		// Выполняется сортировка (один раз для каждого уровня), если в файле уровня события расположены не по порядку (для уже отсортированных уровней ничего не меняется)
		static void SortArenaEvents()
		{
			var events = LevelData.arenaAngles;
            for (int i = 1; i < events.Count; i++)
            {
                if (events[i].time < events[i - 1].time)
                {
                    var sorted = events.OrderBy(e => e.time).ToList();
                    events.Clear();
                    events.AddRange(sorted);
                    Plugin.Log.LogInfo("Arena-angle events were out of order in the level file: sorted " + events.Count);
                    return;
                }
            }
		}

		//	Раз в секунду кольцо сохраняет контрольную точку (время, угол). Ранее сохранялось ТЕКУЩЕЕ время, однако процесс интегрирования
		//     	(с шагом 1/300 с ф) по сути останавливался чуть позже. Из аз чего этот временной отрезок останавливался чуть позже. В результате чего
		//     	временной отрезок учитывался при расчетах дважды, причем величина дублирующегося участка зависела от того, на какой момент времени приходился кадр
		//      Теперь сохраняется точное время, в которое интегрирование было фактически завершено
		//	b) В среднем из-за этого двойного учета кольцо вращалось в 601/600 раза быстрее. Чтобы сохранить исходную скорость, теперь каждый шаг интегрирования
		// 		умножается на этот коэффициент
		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var lastAbsSpinField = AccessTools.Field(typeof(MainGame), "lastAbsSpin");
            var factorField = AccessTools.Field(typeof(MainGame), "factor");
            var matcher = new CodeMatcher(instructions)
			// a) Меняем сохранение чекпоинта на new Vector2(num3, num2) вместо new Vector2(num, num2)
			.MatchForward(false,
                new CodeMatch(OpCodes.Ldloc_0),
                new CodeMatch(OpCodes.Ldloc_1),
                new CodeMatch(OpCodes.Newobj),
                new CodeMatch(OpCodes.Stfld, lastAbsSpinField))
            .ThrowIfInvalid("Не найден паттерн сохранения lastAbsSpin")
            .SetOpcodeAndOperand(OpCodes.Ldloc_2) // Заменяем первый Ldloc_0 на Ldloc_2

			// b) Умножение скорости вращения на 601/600
            .Start() // Сбрасываем поиск в начало
            .MatchForward(false,
                new CodeMatch(OpCodes.Ldfld, factorField),
                new CodeMatch(OpCodes.Div))
            .ThrowIfInvalid("Не найден паттерн деления на factor")
            .Advance(1)
            .Insert(
                new CodeInstruction(OpCodes.Ldc_R4, SpinRate),
                new CodeInstruction(OpCodes.Mul));

			Plugin.Log.LogInfo("absoluteSpin: exact checkpoint and spin rate applied via CodeMatcher");
            return matcher.InstructionEnumeration();
		}
	}
}
