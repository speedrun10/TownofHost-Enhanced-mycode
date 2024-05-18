﻿using AmongUs.GameOptions;
using Hazel;
using UnityEngine;
using static TOHE.Options;
using static TOHE.Translator;

namespace TOHE.Roles.Neutral;

internal class Demon : RoleBase
{
    //===========================SETUP================================\\
    private const int Id = 16200;
    private static readonly HashSet<byte> playerIdList = [];
    public static bool HasEnabled => playerIdList.Any();
    public override bool IsEnable => HasEnabled;
    public override CustomRoles ThisRoleBase => CustomRoles.Impostor;
    public override Custom_RoleType ThisRoleType => Custom_RoleType.NeutralKilling;
    //==================================================================\\

    private static readonly Dictionary<byte, int> PlayerHealth = [];
    private static readonly Dictionary<byte, int> DemonHealth = [];

    private static OptionItem KillCooldown;
    private static OptionItem CanVent;
    private static OptionItem HasImpostorVision;
    private static OptionItem HealthMax;
    private static OptionItem Damage;
    private static OptionItem SelfHealthMax;
    private static OptionItem SelfDamage;

    public override void SetupCustomOption()
    {
        SetupSingleRoleOptions(Id, TabGroup.NeutralRoles, CustomRoles.Demon, 1, zeroOne: false);
        KillCooldown = FloatOptionItem.Create(Id + 10, "DemonKillCooldown", new(1f, 180f, 1f), 2f, TabGroup.NeutralRoles, false).SetParent(CustomRoleSpawnChances[CustomRoles.Demon])
            .SetValueFormat(OptionFormat.Seconds);
        CanVent = BooleanOptionItem.Create(Id + 11, "CanVent", true, TabGroup.NeutralRoles, false).SetParent(CustomRoleSpawnChances[CustomRoles.Demon]);
        HasImpostorVision = BooleanOptionItem.Create(Id + 13, "ImpostorVision", false, TabGroup.NeutralRoles, false).SetParent(CustomRoleSpawnChances[CustomRoles.Demon]);
        HealthMax = IntegerOptionItem.Create(Id + 15, "DemonHealthMax", new(5, 200, 5), 100, TabGroup.NeutralRoles, false).SetParent(CustomRoleSpawnChances[CustomRoles.Demon])
            .SetValueFormat(OptionFormat.Health);
        Damage = IntegerOptionItem.Create(Id + 16, "DemonDamage", new(1, 100, 1), 15, TabGroup.NeutralRoles, false).SetParent(CustomRoleSpawnChances[CustomRoles.Demon])
            .SetValueFormat(OptionFormat.Health);
        SelfHealthMax = IntegerOptionItem.Create(Id + 17, "DemonSelfHealthMax", new(100, 100, 5), 100, TabGroup.NeutralRoles, false).SetParent(CustomRoleSpawnChances[CustomRoles.Demon])
            .SetValueFormat(OptionFormat.Health);
        SelfDamage = IntegerOptionItem.Create(Id + 18, "DemonSelfDamage", new(1, 100, 1), 35, TabGroup.NeutralRoles, false).SetParent(CustomRoleSpawnChances[CustomRoles.Demon])
            .SetValueFormat(OptionFormat.Health);
    }
    public override void Init()
    {
        playerIdList.Clear();
        DemonHealth.Clear();
        PlayerHealth.Clear();
    }
    public override void Add(byte playerId)
    {
        playerIdList.Add(playerId);
        DemonHealth.TryAdd(playerId, SelfHealthMax.GetInt());

        foreach (var pc in Main.AllAlivePlayerControls)
            PlayerHealth.TryAdd(pc.PlayerId, HealthMax.GetInt());

        if (!AmongUsClient.Instance.AmHost) return;
        if (!Main.ResetCamPlayerList.Contains(playerId))
            Main.ResetCamPlayerList.Add(playerId);
    }
    public override void SetKillCooldown(byte id) => Main.AllPlayerKillCooldown[id] = KillCooldown.GetFloat();
    public override void ApplyGameOptions(IGameOptions opt, byte id) => opt.SetVision(HasImpostorVision.GetBool());

    public override bool CanUseKillButton(PlayerControl pc) => true;
    public override bool CanUseImpostorVentButton(PlayerControl player) => CanVent.GetBool();

    private static void SendRPC(byte playerId)
    {
        MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)CustomRPC.SyncRoleSkill, SendOption.Reliable, -1);
        writer.WritePacked((int)CustomRoles.Demon);
        writer.Write(playerId);
        if (DemonHealth.ContainsKey(playerId))
            writer.Write(DemonHealth[playerId]);
        else
            writer.Write(PlayerHealth[playerId]);
        AmongUsClient.Instance.FinishRpcImmediately(writer);
    }
    public override void ReceiveRPC(MessageReader reader, PlayerControl NaN)
    {
        byte PlayerId = reader.ReadByte();
        int Health = reader.ReadInt32();
        if (DemonHealth.ContainsKey(PlayerId))
            DemonHealth[PlayerId] = Health;
        else
            PlayerHealth[PlayerId] = Health;
    }
    public override bool OnCheckMurderAsKiller(PlayerControl killer, PlayerControl target)
    {
        if (killer == null || target == null || !killer.Is(CustomRoles.Demon) || target.Is(CustomRoles.Demon) || !PlayerHealth.ContainsKey(target.PlayerId)) return false;
        killer.SetKillCooldown();

        if (PlayerHealth[target.PlayerId] - Damage.GetInt() < 1)
        {
            PlayerHealth.Remove(target.PlayerId);
            killer.RpcMurderPlayer(target);
            Utils.NotifyRoles(SpecifySeer: killer);
            return false;
        }

        PlayerHealth[target.PlayerId] -= Damage.GetInt();
        SendRPC(target.PlayerId);
        RPC.PlaySoundRPC(killer.PlayerId, Sounds.KillSound);
        Utils.NotifyRoles(SpecifySeer: killer);

        Logger.Info($"{killer.GetNameWithRole()} 对玩家 {target.GetNameWithRole()} 造成了 {Damage.GetInt()} 点伤害", "Demon");
        return false;
    }
    public override bool OnCheckMurderAsTarget(PlayerControl killer, PlayerControl target)
    {
        if (target.IsTransformedNeutralApocalypse()) return true;
        if (killer == null || target == null || !target.Is(CustomRoles.Demon) || killer.Is(CustomRoles.Demon)) return true;

        if (DemonHealth[target.PlayerId] - SelfDamage.GetInt() < 1)
        {
            DemonHealth.Remove(target.PlayerId);
            Utils.NotifyRoles(SpecifySeer: target);
            return true;
        }

        killer.SetKillCooldown();

        DemonHealth[target.PlayerId] -= SelfDamage.GetInt();
        SendRPC(target.PlayerId);
        RPC.PlaySoundRPC(target.PlayerId, Sounds.KillSound);
        killer.RpcGuardAndKill(target);
        Utils.NotifyRoles(SpecifySeer: target);

        Logger.Info($"{killer.GetNameWithRole()} 对玩家 {target.GetNameWithRole()} 造成了 {SelfDamage.GetInt()} 点伤害", "Demon");
        return false;
    }
    public override string GetMark(PlayerControl seer, PlayerControl target = null, bool isForMeeting = false)
    {
        if (!seer.Is(CustomRoles.Demon) || !seer.IsAlive()) return string.Empty;

        if (target != null && seer.PlayerId == target.PlayerId)
        {
            var GetValue = DemonHealth.TryGetValue(target.PlayerId, out var value);
            return GetValue && value > 0 ? Utils.ColorString(GetColor(value, true), $"【{value}/{SelfHealthMax.GetInt()}】") : string.Empty;
        }
        else
        {
            var GetValue = PlayerHealth.TryGetValue(target.PlayerId, out var value);
            return GetValue && value > 0 ? Utils.ColorString(GetColor(value), $"【{value}/{HealthMax.GetInt()}】") : string.Empty;
        }
    }
    private static Color32 GetColor(float Health, bool self = false)
    {
        var x = (int)(Health / (self ? SelfHealthMax.GetInt() : HealthMax.GetInt()) * 10 * 50);
        int R = 255; int G = 255; int B = 0;
        if (x > 255) R -= (x - 255); else G = x;
        return new Color32((byte)R, (byte)G, (byte)B, byte.MaxValue);
    }
    public override void SetAbilityButtonText(HudManager hud, byte playerId)
    {
        hud.KillButton.OverrideText(GetString("DemonButtonText"));
    }
}