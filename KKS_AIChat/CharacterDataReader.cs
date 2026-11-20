using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace KKS_AIChat
{
    public class CharacterSnapshot
    {
        public string UniqueId;
        public string FullName;
        public string Nickname;
        public string CallName;
        public string PlayerInGameName;
        public bool CanUsePetName;
        public int PersonalityId;
        public string PersonalityName;
        public int RelationLevel;
        public string RelationName;
        public int Favor;
        public bool IsGirlfriend;
        public bool IsFirstGirlfriend;
        public bool IsKiss;
        public bool IsDayH;
        public int HCount;
        public int Lewdness;
        public string HExperience;
        public string ExtraStatus;
        public string OutfitCategory;
        public string ClothingSummary;
        public string ProfileText;
        public string HeightComparisonText;

        // Contexto del mundo en TalkScene
        public string TimeOfDay;
        public string DayOfWeek;
        public string CurrentPlace;
        public bool IsOtherPeopleNearby;
        public bool IsFollowingPlayer;
        public bool IsHPossible;
        public bool IsExposure;
        public bool IsFemaleOnlyArea;
        public List<string> AvailableInvitations = new List<string>();
    }

    public static class CharacterDataReader
    {
        private static readonly BindingFlags AllFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;

        private static RectTransform cachedAdvMessageWindow = null;

        private static readonly Dictionary<int, string> PersonalityNamesES = new Dictionary<int, string>
        {
            { 0, "Sexy" }, { 1, "Ojousama" }, { 2, "Altiva" }, { 3, "Kouhai" },
            { 4, "Misteriosa" }, { 5, "Excéntrica" }, { 6, "Yamato Nadeshiko" }, { 7, "Tomboy" },
            { 8, "Pura" }, { 9, "Distraída" }, { 10, "Fantasiosa" }, { 11, "Maternal" },
            { 12, "Hermana mayor" }, { 13, "Gyaru" }, { 14, "Delincuente" }, { 15, "Salvaje" },
            { 16, "Seria / Cool" }, { 17, "Arisca" }, { 18, "Desafortunada" }, { 19, "Intelectual" },
            { 20, "Tímida" }, { 21, "Heroína clásica" }, { 22, "A la moda" }, { 23, "Otaku" },
            { 24, "Yandere" }, { 25, "Perezosa" }, { 26, "Callada" }, { 27, "Obstinada" },
            { 28, "Tradicional" }, { 29, "Modesta" }, { 30, "Amigable" }, { 31, "Caprichosa" },
            { 32, "Honesta" }, { 33, "Glamorosa" }, { 34, "Retornada" }, { 35, "Informal" },
            { 36, "Sádica" }, { 37, "Inexpresiva" }, { 38, "Cuidadosa" }, { 39, "Isleña" }
        };

        private static readonly Dictionary<int, string> PersonalityNamesEN = new Dictionary<int, string>
        {
            { 0, "Sexy" }, { 1, "Ojousama" }, { 2, "Snobby" }, { 3, "Kouhai" },
            { 4, "Mysterious" }, { 5, "Weirdo" }, { 6, "Yamato Nadeshiko" }, { 7, "Tomboy" },
            { 8, "Pure" }, { 9, "Simple" }, { 10, "Delusional" }, { 11, "Motherly" },
            { 12, "Big Sisterly" }, { 13, "Gyaru" }, { 14, "Delinquent" }, { 15, "Wild" },
            { 16, "Wannabe / Cool" }, { 17, "Reluctant" }, { 18, "Jinxed" }, { 19, "Bookish" },
            { 20, "Timid" }, { 21, "Typical Schoolgirl" }, { 22, "Trendy" }, { 23, "Otaku" },
            { 24, "Yandere" }, { 25, "Lazy" }, { 26, "Quiet" }, { 27, "Stubborn" },
            { 28, "Old-fashioned" }, { 29, "Humble" }, { 30, "Friendly" }, { 31, "Willful" },
            { 32, "Honest" }, { 33, "Glamorous" }, { 34, "Returnee" }, { 35, "Slangy" },
            { 36, "Sadistic" }, { 37, "Emotionless" }, { 38, "Careful" }, { 39, "Island Girl" }
        };

        private static readonly string[] ClothSlotNames = new string[]
        {
            "Top", "Bottom", "Bra", "Underwear", "Gloves", "Pantyhose", "Socks", "Shoes"
        };

        public static TalkScene GetActiveTalkScene()
        {
            TalkScene ts = UnityEngine.Object.FindObjectOfType<TalkScene>();
            if (!IsTalkSceneActuallyActive(ts))
            {
                return null;
            }
            return ts;
        }

        public static bool IsTalkSceneActuallyActive(TalkScene talkScene)
        {
            if (talkScene == null || !talkScene.isActiveAndEnabled)
            {
                return false;
            }

            object isActiveObj = TryGetMemberValue(talkScene, "isActive");
            if (isActiveObj is bool && !(bool)isActiveObj)
            {
                return false;
            }

            object isPalyObj = TryGetMemberValue(talkScene, "isPaly");
            if (isPalyObj is bool && !(bool)isPalyObj)
            {
                return false;
            }

            SaveData.Heroine heroine = ExtractHeroineFromObject(talkScene, 2);
            if (heroine == null)
            {
                return false;
            }

            return true;
        }

        public static bool IsNativeDialogBoxOpen()
        {
            if (cachedAdvMessageWindow == null)
            {
                MonoBehaviour[] allMonos = Resources.FindObjectsOfTypeAll<MonoBehaviour>();
                for (int i = 0; i < allMonos.Length; i++)
                {
                    MonoBehaviour mb = allMonos[i];
                    if (mb == null) continue;

                    string fullName = mb.GetType().FullName;
                    if (fullName == "ADV.MainScenario" || fullName == "ADV.TextScenario")
                    {
                        RectTransform rt = TryGetMemberValue(mb, "MessageWindow") as RectTransform;
                        if (rt == null) rt = TryGetMemberValue(mb, "messageWindow") as RectTransform;
                        if (rt != null)
                        {
                            cachedAdvMessageWindow = rt;
                            if (fullName == "ADV.MainScenario") break;
                        }
                    }
                }
            }

            return cachedAdvMessageWindow != null && cachedAdvMessageWindow.gameObject.activeInHierarchy;
        }

        public static SaveData.Heroine GetActiveHeroine()
        {
            TalkScene talkScene = GetActiveTalkScene();
            if (talkScene == null)
            {
                return null;
            }

            return ExtractHeroineFromObject(talkScene, 2);
        }

        public static string GetHeroineCleanName(SaveData.Heroine heroine)
        {
            if (heroine == null || string.IsNullOrEmpty(heroine.Name))
            {
                return TalkSceneController.UseEnglishUI ? "Unknown" : "Desconocida";
            }

            string trimmed = heroine.Name.Trim();
            return string.IsNullOrEmpty(trimmed) ? (TalkSceneController.UseEnglishUI ? "Unknown" : "Desconocida") : trimmed;
        }

        public static string GetHeroineUniqueId(SaveData.Heroine heroine)
        {
            if (heroine == null) return "unknown";

            string fallbackName = GetHeroineCleanName(heroine);
            return GetUniqueId(heroine, fallbackName);
        }

        public static CharacterSnapshot GetCurrentTalkHeroine()
        {
            TalkScene talkScene = GetActiveTalkScene();
            if (talkScene == null)
            {
                return null;
            }

            SaveData.Heroine heroine = ExtractHeroineFromObject(talkScene, 2);
            if (heroine == null)
            {
                return null;
            }

            CharacterSnapshot snapshot = new CharacterSnapshot();
            snapshot.FullName = GetHeroineCleanName(heroine);
            snapshot.Nickname = GetStringMember(heroine, "nickname");
            snapshot.CallName = GetStringMember(heroine, "callName");
            snapshot.PlayerInGameName = GetPlayerInGameName();
            snapshot.UniqueId = GetUniqueId(heroine, snapshot.FullName);
            snapshot.PersonalityId = heroine.personality;
            snapshot.PersonalityName = GetPersonalityName(heroine.personality);
            snapshot.RelationLevel = heroine.relation;
            snapshot.RelationName = GetRelationName(heroine.relation);
            snapshot.Favor = heroine.favor;

            snapshot.IsGirlfriend = GetBoolMember(heroine, "isGirlfriend");
            snapshot.IsFirstGirlfriend = GetBoolMember(heroine, "isFirstGirlfriend");
            snapshot.CanUsePetName = snapshot.IsGirlfriend || snapshot.RelationLevel >= 2;
            snapshot.IsKiss = GetBoolMember(heroine, "isKiss");
            snapshot.IsDayH = GetBoolMember(heroine, "isDayH");
            snapshot.HCount = GetIntMember(heroine, "hCount");
            snapshot.Lewdness = GetIntMember(heroine, "lewdness");
            snapshot.HExperience = TranslateHExperience(TryGetMemberValue(heroine, "HExperience"));
            snapshot.ExtraStatus = BuildExtraStatusText(snapshot);

            snapshot.OutfitCategory = GetOutfitCategory(heroine.chaCtrl);
            snapshot.ClothingSummary = GetClothingSummary(heroine.chaCtrl);
            snapshot.ProfileText = GetKKProfileText(heroine.charFile);
            snapshot.HeightComparisonText = BuildHeightComparison(heroine);

            PopulateWorldContext(talkScene, snapshot);

            return snapshot;
        }

        private static string BuildHeightComparison(SaveData.Heroine heroine)
        {
            try
            {
                float heroineSlider = GetHeightSliderFromChara(heroine != null ? heroine.charFile : null, heroine != null ? heroine.chaCtrl : null);
                object playerObj = GetPlayerObject();
                ChaFile playerFile = TryGetMemberValue(playerObj, "charFile") as ChaFile;
                ChaControl playerCtrl = TryGetMemberValue(playerObj, "chaCtrl") as ChaControl;
                float playerSlider = GetHeightSliderFromChara(playerFile, playerCtrl);

                if (heroineSlider < 0f || playerSlider < 0f)
                {
                    return "";
                }

                float heroineCm = 145f + (heroineSlider * 27f);
                float playerCm = 160f + (playerSlider * 26f);
                float diff = heroineCm - playerCm;

                if (diff > 7f)
                {
                    return "You are noticeably taller than the player (he is shorter than you; you look slightly down at him).";
                }
                if (diff > 2.5f)
                {
                    return "You are slightly taller than the player (he is a bit shorter than you).";
                }
                if (diff < -12f)
                {
                    return "You are much shorter than the player (he is much taller than you; you have to look up at him).";
                }
                if (diff < -2.5f)
                {
                    return "You are shorter than the player (he is taller than you).";
                }

                return "You are roughly the same height as the player (eye to eye).";
            }
            catch
            {
                return "";
            }
        }

        private static float GetHeightSliderFromChara(ChaFile charFile, ChaControl chaCtrl)
        {
            ChaFile targetFile = charFile;
            if (targetFile == null && chaCtrl != null)
            {
                targetFile = TryGetMemberValue(chaCtrl, "chaFile") as ChaFile;
            }

            if (targetFile != null)
            {
                object custom = TryGetMemberValue(targetFile, "custom");
                object body = TryGetMemberValue(custom, "body");
                float[] shapes = TryGetMemberValue(body, "shapeValueBody") as float[];
                if (shapes != null && shapes.Length > 0)
                {
                    return shapes[0];
                }
            }

            if (chaCtrl != null)
            {
                object fileBody = TryGetMemberValue(chaCtrl, "fileBody");
                float[] shapes = TryGetMemberValue(fileBody, "shapeValueBody") as float[];
                if (shapes != null && shapes.Length > 0)
                {
                    return shapes[0];
                }
            }

            return -1f;
        }

        private static object GetPlayerObject()
        {
            try
            {
                Type gameType = FindTypeAcrossAssemblies("Manager.Game");
                if (gameType == null) return null;

                object playerObj = TryGetStaticMember(gameType, "Player");
                if (playerObj != null) return playerObj;

                object saveData = TryGetStaticMember(gameType, "saveData") ?? TryGetStaticMember(gameType, "SaveData");
                if (saveData != null)
                {
                    playerObj = TryGetMemberValue(saveData, "player") ?? TryGetMemberValue(saveData, "Player");
                    if (playerObj != null) return playerObj;
                }

                object instance = TryGetStaticMember(gameType, "instance") ?? TryGetStaticMember(gameType, "Instance");
                if (instance != null)
                {
                    playerObj = TryGetMemberValue(instance, "Player") ?? TryGetMemberValue(instance, "player");
                    if (playerObj != null) return playerObj;

                    object instSave = TryGetMemberValue(instance, "saveData") ?? TryGetMemberValue(instance, "SaveData");
                    if (instSave != null)
                    {
                        playerObj = TryGetMemberValue(instSave, "player") ?? TryGetMemberValue(instSave, "Player");
                        if (playerObj != null) return playerObj;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static void PopulateWorldContext(TalkScene talkScene, CharacterSnapshot snapshot)
        {
            string unknown = TalkSceneController.UseEnglishUI ? "Unknown" : "Desconocido";
            string defaultPlace = TalkSceneController.UseEnglishUI ? "The Island" : "La isla";

            snapshot.TimeOfDay = unknown;
            snapshot.DayOfWeek = "";
            snapshot.CurrentPlace = defaultPlace;
            snapshot.IsOtherPeopleNearby = false;
            snapshot.IsFollowingPlayer = false;
            snapshot.IsHPossible = false;
            snapshot.IsExposure = false;
            snapshot.IsFemaleOnlyArea = false;

            object necessaryInfo = TryGetMemberValue(talkScene, "necessaryInfo");
            if (necessaryInfo != null)
            {
                object cycleObj = TryGetMemberValue(necessaryInfo, "cycleType");
                if (cycleObj != null)
                {
                    snapshot.TimeOfDay = TranslateCycleType(cycleObj.ToString());
                }

                snapshot.IsOtherPeopleNearby = GetBoolMember(necessaryInfo, "isOtherPeople");
                snapshot.IsFollowingPlayer = GetBoolMember(necessaryInfo, "isChase");
                snapshot.IsHPossible = GetBoolMember(necessaryInfo, "isHPossible");
                snapshot.IsExposure = GetBoolMember(necessaryInfo, "exposure");
                snapshot.IsFemaleOnlyArea = GetBoolMember(necessaryInfo, "femaleOnly");

                string[] placeNames = TryGetMemberValue(necessaryInfo, "placeNames") as string[];
                if (placeNames != null && placeNames.Length > 0)
                {
                    List<string> validPlaces = new List<string>();
                    for (int i = 0; i < placeNames.Length; i++)
                    {
                        if (!string.IsNullOrEmpty(placeNames[i]) && !string.IsNullOrEmpty(placeNames[i].Trim()))
                        {
                            validPlaces.Add(placeNames[i].Trim());
                        }
                    }
                    if (validPlaces.Count > 0)
                    {
                        snapshot.CurrentPlace = string.Join(" / ", validPlaces.ToArray());
                    }
                }
            }

            try
            {
                Type actionSceneType = FindTypeAcrossAssemblies("ActionScene");
                if (actionSceneType != null)
                {
                    UnityEngine.Object actionScene = UnityEngine.Object.FindObjectOfType(actionSceneType);
                    if (actionScene != null)
                    {
                        object cycle = TryGetMemberValue(actionScene, "Cycle");
                        if (cycle != null)
                        {
                            object weekObj = TryGetMemberValue(cycle, "nowWeek");
                            if (weekObj != null)
                            {
                                snapshot.DayOfWeek = TranslateWeekDay(weekObj.ToString());
                            }

                            if (snapshot.TimeOfDay == unknown)
                            {
                                object nowType = TryGetMemberValue(cycle, "nowType");
                                if (nowType != null)
                                {
                                    snapshot.TimeOfDay = TranslateCycleType(nowType.ToString());
                                }
                            }
                        }

                        if (snapshot.CurrentPlace == defaultPlace)
                        {
                            object map = TryGetMemberValue(actionScene, "Map");
                            object mapInfo = TryGetMemberValue(map, "Info");
                            string mapName = GetStringMember(mapInfo, "MapName");
                            if (string.IsNullOrEmpty(mapName))
                            {
                                mapName = GetStringMember(mapInfo, "DisplayName");
                            }
                            if (!string.IsNullOrEmpty(mapName))
                            {
                                snapshot.CurrentPlace = mapName;
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            snapshot.AvailableInvitations = TalkSceneController.GetAvailableInvitations(talkScene, snapshot);
        }

        private static string TranslateCycleType(string raw)
        {
            bool en = TalkSceneController.UseEnglishUI;
            if (string.IsNullOrEmpty(raw)) return en ? "Unknown" : "Desconocido";

            switch (raw)
            {
                case "WakeUp":
                case "Morning":
                    return en ? "Morning" : "Mañana";
                case "Daytime":
                    return en ? "Daytime" : "Mediodía / Tarde";
                case "Evening":
                    return en ? "Evening" : "Atardecer";
                case "Night":
                    return en ? "Night" : "Noche";
                case "GotoMyHouse":
                case "MyHouse":
                    return en ? "Night (Inn / Room)" : "Noche (Posada)";
                default:
                    return raw;
            }
        }

        private static string TranslateWeekDay(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            if (TalkSceneController.UseEnglishUI) return raw;

            switch (raw)
            {
                case "Monday": return "Lunes";
                case "Tuesday": return "Martes";
                case "Wednesday": return "Miércoles";
                case "Thursday": return "Jueves";
                case "Friday": return "Viernes";
                case "Saturday": return "Sábado";
                case "Sunday": return "Domingo";
                default: return raw;
            }
        }

        private static string GetPlayerInGameName()
        {
            object playerObj = GetPlayerObject();
            if (playerObj == null) return "";

            string name = GetStringMember(playerObj, "Name");
            if (!string.IsNullOrEmpty(name)) return name;

            string first = GetStringMember(playerObj, "firstname");
            string last = GetStringMember(playerObj, "lastname");
            return (last + " " + first).Trim();
        }

        private static object TryGetStaticMember(Type t, string name)
        {
            if (t == null) return null;

            BindingFlags staticFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy;

            PropertyInfo prop = t.GetProperty(name, staticFlags);
            if (prop != null && prop.CanRead && prop.GetIndexParameters().Length == 0)
            {
                try { return prop.GetValue(null, null); }
                catch { }
            }

            FieldInfo field = t.GetField(name, staticFlags);
            if (field != null)
            {
                try { return field.GetValue(null); }
                catch { }
            }

            return null;
        }

        private static string GetKKProfileText(ChaFile charFile)
        {
            if (charFile == null) return "";

            Type extSaveType = FindTypeAcrossAssemblies("ExtensibleSaveFormat.ExtendedSave");
            if (extSaveType == null) return "";

            MethodInfo getAllMethod = extSaveType.GetMethod(
                "GetAllExtendedData",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new Type[] { typeof(ChaFile) },
                null
            );

            if (getAllMethod == null) return "";

            try
            {
                IDictionary allData = getAllMethod.Invoke(null, new object[] { charFile }) as IDictionary;
                if (allData == null || !allData.Contains("KK_Profile")) return "";

                object pluginData = allData["KK_Profile"];
                if (pluginData == null) return "";

                IDictionary innerDict = TryGetMemberValue(pluginData, "data") as IDictionary;
                if (innerDict == null || !innerDict.Contains("ProfileText")) return "";

                string profile = innerDict["ProfileText"] as string;
                return string.IsNullOrEmpty(profile) ? "" : profile.Trim();
            }
            catch
            {
                return "";
            }
        }

        public static Type FindTypeAcrossAssemblies(string fullTypeName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                try
                {
                    Type found = assemblies[i].GetType(fullTypeName, false);
                    if (found != null)
                    {
                        return found;
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        public static SaveData.Heroine ExtractHeroineFromObject(object target, int depthRemaining)
        {
            if (target == null || depthRemaining <= 0) return null;

            Type type = target.GetType();
            List<object> nestedCandidates = new List<object>();

            FieldInfo[] fields = type.GetFields(AllFlags);
            foreach (FieldInfo field in fields)
            {
                object val = null;
                try { val = field.IsStatic ? field.GetValue(null) : field.GetValue(target); }
                catch { continue; }

                if (val == null) continue;

                SaveData.Heroine direct = val as SaveData.Heroine;
                if (direct != null) return direct;

                if (depthRemaining > 1 && ShouldInspectNested(val))
                {
                    nestedCandidates.Add(val);
                }
            }

            PropertyInfo[] props = type.GetProperties(AllFlags);
            foreach (PropertyInfo prop in props)
            {
                if (!prop.CanRead || prop.GetIndexParameters().Length > 0) continue;

                MethodInfo getter = prop.GetGetMethod(true);
                if (getter == null) continue;

                object val = null;
                try { val = getter.IsStatic ? prop.GetValue(null, null) : prop.GetValue(target, null); }
                catch { continue; }

                if (val == null) continue;

                SaveData.Heroine direct = val as SaveData.Heroine;
                if (direct != null) return direct;

                if (depthRemaining > 1 && ShouldInspectNested(val))
                {
                    nestedCandidates.Add(val);
                }
            }

            foreach (object candidate in nestedCandidates)
            {
                SaveData.Heroine nested = ExtractHeroineFromObject(candidate, depthRemaining - 1);
                if (nested != null) return nested;
            }

            return null;
        }

        private static bool ShouldInspectNested(object val)
        {
            Type t = val.GetType();
            if (t.IsPrimitive || t.IsEnum || t == typeof(string) || t.IsArray) return false;

            string ns = t.Namespace ?? "";
            if (ns.StartsWith("System") || ns.StartsWith("UnityEngine") || ns.StartsWith("TMPro")) return false;

            return true;
        }

        private static string GetUniqueId(SaveData.Heroine heroine, string fallbackName)
        {
            if (heroine.charFile != null)
            {
                object about = TryGetMemberValue(heroine.charFile, "about");
                string dataId = GetStringMember(about, "dataID");
                if (!string.IsNullOrEmpty(dataId)) return dataId;

                if (!string.IsNullOrEmpty(heroine.charFile.charaFileName))
                {
                    return heroine.charFile.charaFileName;
                }
            }

            return fallbackName + "_P" + heroine.personality;
        }

        private static string BuildExtraStatusText(CharacterSnapshot s)
        {
            bool en = TalkSceneController.UseEnglishUI;
            List<string> parts = new List<string>();
            parts.Add((en ? "Lover: " : "Novia: ") + (s.IsGirlfriend ? (en ? "Yes" : "Sí") : "No"));
            parts.Add((en ? "Kiss: " : "Beso: ") + (s.IsKiss ? (en ? "Yes" : "Sí") : "No"));
            parts.Add((en ? "H Count: " : "Escenas H: ") + s.HCount);
            if (s.IsDayH)
            {
                parts.Add(en ? "Intimate today: Yes" : "Intimidad hoy: Sí");
            }
            return string.Join(" | ", parts.ToArray());
        }

        private static string TranslateHExperience(object rawValue)
        {
            if (rawValue == null) return "Unknown";

            string text = rawValue.ToString();
            if (text == "初めて") return "First time / Inexperienced";
            if (text == "不慣れ") return "Unaccustomed / Shy";
            if (text == "慣れ") return "Accustomed / Experienced";
            if (text == "淫乱") return "Very bold / Passionate";
            return text;
        }

        private static string GetStringMember(object target, string name)
        {
            string val = TryGetMemberValue(target, name) as string;
            return string.IsNullOrEmpty(val) ? "" : val.Trim();
        }

        public static bool GetBoolMember(object target, string name)
        {
            object val = TryGetMemberValue(target, name);
            return val is bool && (bool)val;
        }

        private static int GetIntMember(object target, string name)
        {
            object val = TryGetMemberValue(target, name);
            if (val is int) return (int)val;
            return 0;
        }

        public static object TryGetMemberValue(object target, string name)
        {
            if (target == null) return null;

            Type t = target.GetType();

            FieldInfo field = t.GetField(name, AllFlags);
            if (field != null)
            {
                try { return field.IsStatic ? field.GetValue(null) : field.GetValue(target); }
                catch { return null; }
            }

            PropertyInfo prop = t.GetProperty(name, AllFlags);
            if (prop != null && prop.CanRead && prop.GetIndexParameters().Length == 0)
            {
                MethodInfo getter = prop.GetGetMethod(true);
                if (getter != null)
                {
                    try { return getter.IsStatic ? prop.GetValue(null, null) : prop.GetValue(target, null); }
                    catch { return null; }
                }
            }

            return null;
        }

        private static string GetPersonalityName(int personalityId)
        {
            Dictionary<int, string> map = TalkSceneController.UseEnglishUI ? PersonalityNamesEN : PersonalityNamesES;
            string name;
            if (map.TryGetValue(personalityId, out name))
            {
                return name + " (ID: " + personalityId + ")";
            }

            return (TalkSceneController.UseEnglishUI ? "Unknown" : "Desconocida") + " (ID: " + personalityId + ")";
        }

        private static string GetRelationName(int relation)
        {
            bool en = TalkSceneController.UseEnglishUI;
            switch (relation)
            {
                case -1: return en ? "Stranger" : "Sin conocer";
                case 0: return en ? "Acquaintance" : "Conocido";
                case 1: return en ? "Friend" : "Amigo";
                case 2: return en ? "Lover" : "Pareja";
                case 3: return en ? "Intimate Lover" : "Pareja íntima";
                default: return (en ? "Level " : "Nivel ") + relation;
            }
        }

        private static string GetOutfitCategory(ChaControl chaCtrl)
        {
            if (chaCtrl == null || chaCtrl.fileStatus == null) return "Unknown";

            int coordType = chaCtrl.fileStatus.coordinateType;
            switch (coordType)
            {
                case 0: return "Casual Clothes";
                case 1: return "Swimsuit";
                case 2: return "Pajamas";
                case 3: return "Bathing / Towel";
                default: return "Outfit " + (coordType + 1);
            }
        }

        private static string GetClothingSummary(ChaControl chaCtrl)
        {
            if (chaCtrl == null || chaCtrl.fileStatus == null || chaCtrl.fileStatus.clothesState == null)
            {
                return "Unavailable";
            }

            byte[] states = chaCtrl.fileStatus.clothesState;
            ChaFileClothes.PartsInfo[] parts = null;
            Array infoClothes = TryGetMemberValue(chaCtrl, "infoClothes") as Array;

            if (chaCtrl.nowCoordinate != null && chaCtrl.nowCoordinate.clothes != null)
            {
                parts = chaCtrl.nowCoordinate.clothes.parts;
            }

            List<string> details = new List<string>();
            int count = Mathf.Min(ClothSlotNames.Length, states.Length);

            for (int i = 0; i < count; i++)
            {
                if (parts != null && i < parts.Length && parts[i].id == 0) continue;

                string itemName = GetClothItemName(infoClothes, i);
                string stateText = TranslateClothState(states[i]);

                if (!string.IsNullOrEmpty(itemName))
                {
                    details.Add(ClothSlotNames[i] + ": " + itemName + " (" + stateText + ")");
                }
                else
                {
                    details.Add(ClothSlotNames[i] + ": " + stateText);
                }
            }

            if (details.Count == 0) return "No clothes equipped";

            return string.Join(" | ", details.ToArray());
        }

        private static string GetClothItemName(Array infoClothes, int slotIndex)
        {
            if (infoClothes == null || slotIndex < 0 || slotIndex >= infoClothes.Length) return "";

            object infoItem = infoClothes.GetValue(slotIndex);
            if (infoItem == null) return "";

            string name = TryGetMemberValue(infoItem, "Name") as string;
            if (string.IsNullOrEmpty(name))
            {
                name = TryGetMemberValue(infoItem, "name") as string;
            }

            if (string.IsNullOrEmpty(name) || name == "なし" || name.Equals("None", StringComparison.OrdinalIgnoreCase))
            {
                return "";
            }

            return name.Trim();
        }

        private static string TranslateClothState(byte state)
        {
            switch (state)
            {
                case 0: return "on";
                case 1:
                case 2: return "half-off";
                case 3: return "off";
                default: return "state " + state;
            }
        }
    }
}