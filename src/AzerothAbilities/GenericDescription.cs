namespace AzerothAbilities;

internal static class GenericDescription
{
    internal static string Spell(LearnedSpell spell)
    {
        var d=spell.Definition;
        string text=$"{d.Name} - Rank {spell.Rank}\n";
        if(d.Kind=="passive")return text+Passive(spell);
        if(d.BaseDamage>0)text+=$"{AbilityRuntime.Damage(spell):0.##} {d.School} damage";
        if(d.Targets>0)text+=$" - Targets: {ClassSpellBehaviors.Quantity(spell)}";
        if(d.Kind=="damageOverTime")
        {float duration=ClassSpellBehaviors.Duration(spell);int ticks=Math.Max(1,(int)MathF.Round(duration/Math.Max(.01f,d.Duration)*Math.Max(1,d.Ticks),MidpointRounding.AwayFromZero));text+=$"\n{AbilityRuntime.Damage(spell)*(duration/Math.Max(.01f,d.Duration))/ticks:0.##} per tick - {ticks} ticks";}
        else if(d.Kind is "groundAoe" or "totem")text+=$"\n{AbilityRuntime.Damage(spell)/Math.Max(1,d.Ticks):0.##} per tick - {ClassSpellBehaviors.Duration(spell):0.##} sec";
        if(d.Kind=="finisher")text+="\nDouble damage below 30% enemy health";
        if(d.StatusKind>0)text+="\n"+Status(spell);
        if(d.Radius>0)text+=$"\nRadius: {ClassSpellBehaviors.Radius(spell):0.##}";
        if(d.Range>0)text+=$"\nRange: {(d.Kind=="cone"?ClassSpellBehaviors.ConeReach(spell,d.Range):d.Range):0.##}";
        return text+$"\nCooldown: {AbilityRuntime.Cooldown(spell):0.##} sec";
    }
    static string Status(LearnedSpell spell)
    {
        var d=spell.Definition;float factor=MathF.Sqrt(Math.Max(0,spell.DamageFactor*(1+ClassCombat.SpellBonus(spell,0))));
        float duration=ClassSpellBehaviors.Duration(spell),status=ClassSpellBehaviors.StatusFactor(spell);
        float scale=d.Kind is "selfBuff" or "barrier"?(AbilityRuntime.Catalog?.DamageScale??1.4f)*factor*(1+ClassCombat.StatBonus(23)+ClassCombat.TalentStat(23)):status;
        return d.StatusKind switch
        {
            1=>$"Slows {Math.Min(.9f,d.StatusValue*status)*100:0.#}% for {d.StatusDuration*TomeCompatibility.EffectDuration:0.##} sec",
            2=>$"Roots for {d.StatusValue*TomeCompatibility.EffectDuration:0.##} sec",
            3=>$"Stuns for {d.StatusValue*TomeCompatibility.EffectDuration:0.##} sec",
            4=>$"Fear for {d.StatusValue*TomeCompatibility.EffectDuration:0.##} sec",
            5=>$"Heals {d.StatusValue*status*100:0.#}% of damage dealt",
            6=>$"Heals {d.StatusValue*scale*100:0.#}% max health"+(duration>0?$" over {duration:0.##} sec":""),
            7=>$"Shield absorbs {d.StatusValue*scale*100:0.#}% max health"+(duration>0?$" for {duration:0.##} sec":""),
            8=>$"+{d.StatusValue*status*100:0.#}% damage for {duration:0.##} sec",
            9=>$"+{d.StatusValue*status*100:0.#}% haste for {duration:0.##} sec",
            10=>$"Reduces enemy damage by {Math.Min(.9f,d.StatusValue*status)*100:0.#}% for {d.StatusDuration*TomeCompatibility.EffectDuration:0.##} sec",
            11=>$"Immunity for {duration:0.##} sec when health falls below 35%",
            12=>$"Returns {d.StatusValue*spell.DamageFactor*(1+ClassCombat.SpellBonus(spell,0)):0.##} damage on melee contact for {duration:0.##} sec",
            _=>""
        };
    }
    static string Passive(LearnedSpell spell)
    {
        float damage=AbilityRuntime.Damage(spell),radius=ClassSpellBehaviors.Radius(spell),cd=AbilityRuntime.Cooldown(spell);int quantity=ClassSpellBehaviors.Quantity(spell);
        float rank=MathF.Sqrt(Math.Max(0,spell.DamageFactor*(1+ClassCombat.SpellBonus(spell,0))));
        return spell.Definition.Id switch
        {
            "bloodrage"=>$"Hits build rage up to {100/cd:0.##} per second. At 100 rage, deals {damage:0.##} Physical damage within {radius:0.##}. Cooldown {cd:0.##} sec; loses 15 rage/sec after 3 sec without hits.",
            "redemption"=>$"Kills can burst for {damage:0.##} Holy damage within {radius:0.##}. At most two bursts per second; a burst may cause one additional burst.",
            "hunters_mark"=>$"Marks the toughest target within {spell.Definition.Range:0.##}: +{Math.Min(.4f,.2f*rank)*100:0.#}% damage taken. Death explodes for {damage:0.##} Arcane damage within {radius:0.##}; cooldown {cd:0.##} sec.",
            "seal_fate"=>$"Hits build combo points, critical hits two, up to {5/cd:0.##}/sec. At 5 points deals {damage:0.##} Physical damage to {Math.Min(3,quantity)} targets; doubled below 30% health.",
            "lightwell"=>$"When enemies approach within 25, creates a stationary Lightwell every {cd:0.##} sec. Lasts {ClassSpellBehaviors.Duration(spell):0.##} sec and strikes {quantity} targets within {spell.Definition.Range:0.##} for {damage:0.##} Holy damage each second.",
            "call_of_flame"=>$"Expired totems explode for {damage:0.##} Fire damage within {radius:0.##}; cooldown {cd:0.##} sec.",
            "polymorph"=>$"12% spell-hit chance to polymorph for {10*TomeCompatibility.EffectDuration:0.##} sec; up to 3 sheep. Next hit explodes for 15% max health, capped at {damage:0.##}; radius {radius:0.##}. Bosses are immune.",
            "soul_fire"=>$"Kills gather one soul shard every {cd/5:0.##} sec. At 5 shards, strikes {quantity} targets within {spell.Definition.Range:0.##} for {damage:0.##} Fire damage.",
            "faerie_fire"=>$"Marks {quantity} targets within {spell.Definition.Range:0.##}. Hits arc {Math.Min(1.2f,.8f*rank)*100:0.#}% damage, up to {damage:0.##}, to two nearby targets within {radius:0.##}; four arcs per second per mark. Marks transfer on death.",
            _=>spell.Definition.SourcePassive?.DescriptionTemplate??spell.Definition.Name
        };
    }
}
