using Mutagen.Bethesda.Skyrim;

namespace Boutique.Utilities;

public static class NpcDataExtractor
{
  public static string? GetName(INpcGetter npc) => npc.Name?.String;

  public static bool IsChildRace(string? raceEditorId)
  {
    if (string.IsNullOrWhiteSpace(raceEditorId))
    {
      return false;
    }

    // Common child race patterns in Skyrim
    return raceEditorId.Contains("Child", StringComparison.OrdinalIgnoreCase) ||
           raceEditorId.Contains("DA13", StringComparison.OrdinalIgnoreCase); // Daedric child form
  }

  public static short ExtractLevel(INpcGetter npc) =>
    npc.Configuration.Level is NpcLevel npcLevel ? npcLevel.Level : (short)1;

  public static (bool IsFemale, bool IsUnique, bool IsSummonable, bool IsLeveled) ExtractTraits(INpcGetter npc)
  {
    var config = npc.Configuration;
    return (
      IsFemale: config.Flags.HasFlag(NpcConfiguration.Flag.Female),
      IsUnique: config.Flags.HasFlag(NpcConfiguration.Flag.Unique),
      IsSummonable: config.Flags.HasFlag(NpcConfiguration.Flag.Summonable),
      IsLeveled: config.Level is PcLevelMult);
  }

  /// <summary>
  ///   Extracts NPC skill values. Returns an array of 24 skill values indexed by SPID skill index.
  ///   SPID skill indices: 6=OneHanded, 7=TwoHanded, 8=Marksman, 9=Block, 10=Smithing,
  ///   11=HeavyArmor, 12=LightArmor, 13=Pickpocket, 14=Lockpicking, 15=Sneak,
  ///   16=Alchemy, 17=Speechcraft, 18=Alteration, 19=Conjuration, 20=Destruction,
  ///   21=Illusion, 22=Restoration, 23=Enchanting
  /// </summary>
  public static byte[] ExtractSkillValues(INpcGetter npc)
  {
    var skills = new byte[24];

    if (npc.PlayerSkills == null)
    {
      return skills;
    }

    var skillValues = npc.PlayerSkills.SkillValues;

    skills[6] = GetSkillValue(skillValues, Skill.OneHanded);
    skills[7] = GetSkillValue(skillValues, Skill.TwoHanded);
    skills[8] = GetSkillValue(skillValues, Skill.Archery);
    skills[9] = GetSkillValue(skillValues, Skill.Block);
    skills[10] = GetSkillValue(skillValues, Skill.Smithing);
    skills[11] = GetSkillValue(skillValues, Skill.HeavyArmor);
    skills[12] = GetSkillValue(skillValues, Skill.LightArmor);
    skills[13] = GetSkillValue(skillValues, Skill.Pickpocket);
    skills[14] = GetSkillValue(skillValues, Skill.Lockpicking);
    skills[15] = GetSkillValue(skillValues, Skill.Sneak);
    skills[16] = GetSkillValue(skillValues, Skill.Alchemy);
    skills[17] = GetSkillValue(skillValues, Skill.Speech);
    skills[18] = GetSkillValue(skillValues, Skill.Alteration);
    skills[19] = GetSkillValue(skillValues, Skill.Conjuration);
    skills[20] = GetSkillValue(skillValues, Skill.Destruction);
    skills[21] = GetSkillValue(skillValues, Skill.Illusion);
    skills[22] = GetSkillValue(skillValues, Skill.Restoration);
    skills[23] = GetSkillValue(skillValues, Skill.Enchanting);

    return skills;
  }

  private static byte GetSkillValue(IReadOnlyDictionary<Skill, byte> skillValues, Skill skill) =>
    skillValues.GetValueOrDefault(skill, (byte)0);
}
