using System.Diagnostics;
using System.Reflection;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.FieldTypes;
using UAssetAPI.Kismet.Bytecode;
using UAssetAPI.Kismet.Bytecode.Expressions;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;
using UAssetAPI.Unversioned;

var usmap = new Usmap("Mappings.usmap");
// patch broken struct references in ModLibrary
{
    var modLibrary = new UAsset(@".\Sandfall\Windows\Sandfall\Content\Coop\ModLibrary.uasset", EngineVersion.VER_UE5_4, usmap);
    var partySlot = new UAsset(@".\Export\Sandfall/Content/jRPGTemplate/Structures/S_jRPG_PartySlot.uasset", EngineVersion.VER_UE5_4, usmap);
    var characterHardcodedName = modLibrary.GetNameMapIndexList().First(i => i.Value.StartsWith("CharacterHardcodedName"));
    characterHardcodedName.Value = partySlot.GetNameMapIndexList().First(i => i.Value.StartsWith("CharacterHardcodedName")).Value;
    var battleStartParams = new UAsset(@".\Export\Sandfall/Content/Gameplay/Battle/FBattleStartParams.uasset", EngineVersion.VER_UE5_4, usmap);
    var overrideParty = modLibrary.GetNameMapIndexList().First(i => i.Value.StartsWith("OverrideParty"));
    overrideParty.Value = battleStartParams.GetNameMapIndexList().First(i => i.Value.StartsWith("OverrideParty")).Value;
    modLibrary.Write(@".\Sandfall\Windows\Sandfall\Content\Coop\ModLibrary.uasset");
    Console.WriteLine("Patched ModLibrary");
}

// patch ModActor so we can set GameMode->PlayerControllerClass
{
    var modActor = new UAsset(@".\Sandfall\Windows\Sandfall\Content\Coop\ModActor.uasset", EngineVersion.VER_UE5_4, usmap);
    var setterFunc = (FunctionExport)modActor["set_PlayerControllerClass"];

    var name = new FName(modActor, modActor.AddNameReference(new FString(setterFunc.ObjectName.ToString()[4..])));
    var funcIndex = FPackageIndex.FromExport(modActor.Exports.IndexOf(setterFunc));
    var targetObj = (FObjectProperty)setterFunc.LoadedProperties[0];
    var newVal = setterFunc.LoadedProperties[1];

    // unreal engine doesn't check at runtime whether a property is BlueprintReadOnly, so we can just create a function that sets the GameMode->PlayerControllerClass
    var setterExpression =
        new EX_LetObj {
            AssignmentExpression =
                new EX_LocalVariable { Variable = new KismetPropertyPointer { New = new FFieldPath { Path = [newVal.Name], ResolvedOwner = funcIndex } } },
            VariableExpression = new EX_Context {
                ContextExpression =
                    new EX_InstanceVariable {
                        Variable = new KismetPropertyPointer { New = new FFieldPath { Path = [name], ResolvedOwner = targetObj.PropertyClass } }
                    },
                ObjectExpression =
                    new EX_LocalVariable {
                        Variable = new KismetPropertyPointer { New = new FFieldPath { Path = [targetObj.Name], ResolvedOwner = funcIndex } }
                    },
                Offset = 9,
                RValuePointer = new KismetPropertyPointer { New = new FFieldPath { Path = [targetObj.Name], ResolvedOwner = funcIndex } }
            }
        };
    var @return = new EX_Return { ReturnExpression = new EX_Nothing() };
    var endOfScript = new EX_EndOfScript();
    setterFunc.ScriptBytecode = [setterExpression, @return, endOfScript];

    typeof(UAsset).GetField("NamesReferencedFromExportDataCount", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(modActor,
        modActor.GetNameMapIndexList().Count);

    modActor.Write(@".\Sandfall\Windows\Sandfall\Content/Coop/ModActor.uasset");
    Console.WriteLine("Patched ModActor");
}

// hook blueprints
{
    string[] filesToHook = [
        "jRPGTemplate/Blueprints/Basics/BP_jRPG_Character_Battle_Base",
        "jRPGTemplate/Blueprints/Basics/BP_jRPG_Controller_World",
        "jRPGTemplate/Blueprints/Basics/BP_jRPG_GM_World",
        "UI/Widgets/MainMenu/WBP_MM_MainMenu",
        "UI/Widgets/InGame_Menu/Team/WBP_TeamCard_CharacterStatus",
        "UI/Widgets/InGame_Menu/Team/CharacterSelector/WBP_CharacterCardButton",
        "UI/Widgets/InGame_Menu/Settings/OptionsList/WBP_Settings_GeneralOptionsList",
        "UI/Widgets/HUD_Battle/SubWidget/BattlePopUp_Widgets/WBP_HUD_BattleActionTooltip"
    ];

    foreach (var file in filesToHook) {
        var proc = Process.Start(Path.Join(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "ue-hook-blueprints.exe"),
        [
            $@".\Sandfall\Windows\Sandfall\Content\{file}.uasset",
            $@".\Export\Sandfall\Content\{file}.uasset",
            "--ueversion", "VER_UE5_4",
            "--mappings", "Mappings.usmap"
        ]);
        proc.WaitForExit();
        if (proc.ExitCode != 0)
            throw new Exception($"ue-hook-blueprints.exe failed with {proc.ExitCode}");
    }

    // party-size mods put allies on battle spots 3 and 4, past the end of the game's ally table [1,0,2];
    // this pack ships the battle blueprint, so it carries the longer table itself. A 3-member party
    // never reads the added entries.
    {
        const string battleBase = @".\Sandfall\Windows\Sandfall\Content\jRPGTemplate\Blueprints\Basics\BP_jRPG_Character_Battle_Base.uasset";
        var asset = new UAsset(battleBase, EngineVersion.VER_UE5_4, usmap);
        var function = asset.Exports.OfType<FunctionExport>()
            .Single(f => f.ObjectName.ToString() == "GetBattleSpotIndexLeftToRight_(WhenFacingCharacters)");
        int?[] gameTable = [1, 0, 2];
        var allies = function.ScriptBytecode.OfType<EX_SetArray>()
            .SingleOrDefault(a => a.Elements.Select(e => (e as EX_IntConst)?.Value).SequenceEqual(gameTable))
            ?? throw new Exception("ally left-to-right table [1,0,2] not found");
        allies.Elements = [..allies.Elements, new EX_IntConst { Value = 0 }, new EX_IntConst { Value = 1 }];

        // the switch stores absolute offsets, which follow from the new sizes; jumps would need their targets tracked
        var starts = new Dictionary<KismetExpression, uint>(ReferenceEqualityComparer.Instance);
        uint offset = 0;
        foreach (var statement in function.ScriptBytecode)
            statement.Visit(asset, ref offset, (expression, at) => starts[expression] = at);
        foreach (var expression in starts.Keys) {
            switch (expression) {
                case EX_Jump or EX_JumpIfNot or EX_PushExecutionFlow or EX_SkipOffsetConst or EX_Skip:
                    throw new Exception($"{expression.Inst} in {function.ObjectName} stores an offset this patch does not move");
                case EX_SwitchValue switchValue:
                    for (var i = 0; i < switchValue.Cases.Length; i++) {
                        var next = i + 1 < switchValue.Cases.Length ? switchValue.Cases[i + 1].CaseIndexValueTerm : switchValue.DefaultTerm;
                        switchValue.Cases[i].NextOffset = starts[next];
                    }
                    switchValue.EndGotoOffset = starts[expression] + (uint)expression.GetSize(asset);
                    break;
            }
        }
        asset.Write(battleBase);
        Console.WriteLine("Patched BP_jRPG_Character_Battle_Base");
    }

    var retocProc = Process.Start("retoc.exe",
    [
        "to-zen", @".\Sandfall\Windows", "expedition33_coop_P.utoc",
        "--version", "UE5_4",
        "--filter", "Content/Coop",
        ..filesToHook.SelectMany<string, string>(f => ["--filter", f])
    ]);
    retocProc.WaitForExit();
    if (retocProc.ExitCode != 0)
        throw new Exception($"retoc.exe failed with {retocProc.ExitCode}");
}

// create rebinduibutton addon that changes held actions from A to X since the X button is slightly bugged in menus for players 2 and 3
{
    string[] actionTables = [
        "UI/CommonUI/DataTables/DT_CommonUISpecificAction",
        "UI/CommonUI/DataTables/DT_CommonUIActionComposite"
    ];
    string[] heldActions = [
        "CommonUI_GameMenu_SkillTree_LearnSkill", "CommonUI_GameMenu_SkillTree_AssignSkill",
        "CommonUI_GameMenu_UpgradeWeapon", "CommonUI_GameMenu_AssignAttributes"
    ];
    foreach (var tableAsset in actionTables) {
        var table = new UAsset($@".\Export\Sandfall\Content\{tableAsset}.uasset", EngineVersion.VER_UE5_4, usmap);
        var leftKeyName = new FName(table, table.SearchNameReference(FString.FromString("Gamepad_FaceButton_Left")));
        var export = (DataTableExport)(table.Exports.Find(e => e is DataTableExport) ?? throw new Exception("Missing DataTableExport"));
        foreach (var key in heldActions) {
            var entry = export.Table.Data.Find(entry => entry.Name.Value.Value == key) ?? throw new Exception("Missing DataTable Entry");
            var keyName = ((entry["DefaultGamepadInputTypeInfo"] as StructPropertyData)?["Key"] as StructPropertyData)?["KeyName"] as NamePropertyData ?? throw new Exception("Missing DefaultGamepadInputTypeInfo->Key->KeyName");
            if (keyName.Value.Value.Value != "Gamepad_FaceButton_Bottom")
                throw new Exception("Key is not Gamepad_FaceButton_Bottom");
            keyName.Value = leftKeyName;
        }
        table.Write($@".\Sandfall\Windows\Sandfall\Content\{tableAsset}.uasset");
    }

    var retocProc = Process.Start("retoc.exe",
    [
        "to-zen", @".\Sandfall\Windows", "expedition33_coop_rebinduibuttons_P.utoc",
        "--version", "UE5_4",
        ..actionTables.SelectMany<string, string>(f => ["--filter", f])
    ]);
    retocProc.WaitForExit();
    if (retocProc.ExitCode != 0)
        throw new Exception($"retoc.exe failed with {retocProc.ExitCode}");
}