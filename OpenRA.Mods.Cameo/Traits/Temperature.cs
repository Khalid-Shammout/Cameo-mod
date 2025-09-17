#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using OpenRA.Graphics;
using System.Collections.Generic;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Cameo.Traits
{
	[Desc("Grants a shield with its own health pool. Main health pool is unaffected by damage until the shield is broken.")]
	public class TemperatureInfo : PausableConditionalTraitInfo
	{
		[Desc("The strength of the shield (amount of damage it will absorb).")]
		public readonly int MaxTemperature = 0;

		[Desc("The strength of the shield (amount of damage it will absorb) in percentage of health.")]
		public readonly int MaxPercentageTemperature = 100;

		[Desc("Delay in ticks before shield regenerate for the first time after trait is enabled.")]
		public readonly int InitialRegenDelay = 0;

		[Desc("Delay in ticks after absorbing damage before the shield will regenerate.")]
		public readonly int DamageRegenDelay = 0;

		[Desc("Amount to recharge at each interval.")]
		public readonly int RegenAmount = 0;

		[Desc("Amount to recharge at each interval.")]
		public readonly int PercentageRegenAmount = 0;

		[Desc("Number of ticks between recharging.")]
		public readonly int RegenInterval = 25;

		[Desc("Damage types that ignore this shield.")]
		public readonly BitSet<DamageType> ChangeTemperatureDamageTypes = default;

		[Desc("Damage types when cold.")]
		public readonly BitSet<DamageType> ColdDamageTypes = default;

		[Desc("Damage types when hot.")]
		public readonly BitSet<DamageType> HotDamageTypes = default;

		[Desc("Maximum speed penalty.")]
		public readonly int MaxSpeedModifier = 100;

		[Desc("Maximum damage penalty.")]
		public readonly int MaxDamageModifier = 100;

		[Desc("Maximum damage dealt.")]
		public readonly int MaxDamagePercentageStep = 10;

		[Desc("Time in ticks to wait between each damage infliction.")]
		public readonly int DamageInterval = 5;

		[GrantedConditionReference]
		[Desc("Condition to grant when cool.")]
		public readonly string CoolCondition = null;

		[GrantedConditionReference]
		[Desc("Condition to grant when warm.")]
		public readonly string WarmCondition = null;

		[Desc("Color to overlay at lowest temperature.")]
		public readonly Color CoolColor = Color.FromArgb(170, 190, 255, 100);

		[Desc("Color to overlay at highest temperature.")]
		public readonly Color WarmColor = Color.FromArgb(255, 102, 34, 100);

		[Desc("Hides selection bar when shield is at max strength.")]
		public readonly bool HideBarWhenFull = false;

		public readonly bool ShowSelectionBar = true;
		public readonly Color SelectionBarColor = Color.FromArgb(128, 200, 255);

		public override object Create(ActorInitializer init) { return new Temperature(init, this); }
	}

	public class Temperature : PausableConditionalTrait<TemperatureInfo>, ITick, ISync, ISelectionBar, IDamageModifier, INotifyDamage, ISpeedModifier, ITurnSpeedModifier, ITurretTurnSpeedModifier, IRenderModifier
	{
		int conditionToken = Actor.InvalidConditionToken;
		readonly Actor self;

		[Sync]
		public int CurrentTemperature;
		public int MaxTemperature;
		int ticks;
		int damageTicks;
		readonly float3 coolTint;
		readonly float coolAlpha;
		readonly float3 warmTint;
		readonly float warmAlpha;
		int percentageRegen;

		IHealth health;

		public Temperature(ActorInitializer init, TemperatureInfo info)
			: base(info)
		{
			self = init.Self;
			coolTint = new float3(info.CoolColor.R, info.CoolColor.G, info.CoolColor.B) / 255f;
			coolAlpha = info.CoolColor.A / 255f;
			warmTint = new float3(info.WarmColor.R, info.WarmColor.G, info.WarmColor.B) / 255f;
			warmAlpha = info.WarmColor.A / 255f;
		}

		protected override void Created(Actor self)
		{
			base.Created(self);
			health = self.TraitOrDefault<IHealth>();
			MaxTemperature = Info.MaxTemperature + Info.MaxPercentageTemperature * health.MaxHP / 100;
			CurrentTemperature = 0;
			ticks = Info.InitialRegenDelay;
			damageTicks = Info.DamageInterval;
			percentageRegen = Info.PercentageRegenAmount * MaxTemperature / 100;
		}

		void ITick.Tick(Actor self)
		{
			Equilibrate(self);
		}

		protected void Equilibrate(Actor self)
		{
			if (IsTraitDisabled || IsTraitPaused || CurrentTemperature == 0)
				return;

			if (Info.MaxDamagePercentageStep != 0)
			{
				if (damageTicks > 0)
				{
					--damageTicks;
				}
				else
				{
					damageTicks = Info.DamageInterval;
					InflictDamage(self);
				}
			}

			if (--ticks > 0)
				return;

			if (CurrentTemperature == MaxTemperature)
				return;

			var regenAmount = Info.RegenAmount + percentageRegen;
			if (Math.Abs(CurrentTemperature) < regenAmount)
				regenAmount = CurrentTemperature;

			if (CurrentTemperature > 0)
				ChangeTemperature(self, -regenAmount);
			else if (CurrentTemperature < 0)
				ChangeTemperature(self, regenAmount);

			ticks = Info.RegenInterval;
		}

		public void ChangeTemperature(Actor self, int amount)
		{
			if (IsTraitDisabled || IsTraitPaused)
				return;

			CurrentTemperature += amount;

			if (CurrentTemperature > MaxTemperature)
				CurrentTemperature = MaxTemperature;

			if (CurrentTemperature < -MaxTemperature)
				CurrentTemperature = -MaxTemperature;
		}

		void InflictDamage(Actor self)
		{
			var damageTypes = (CurrentTemperature > 0) ? Info.HotDamageTypes : Info.ColdDamageTypes;
			var damage = (int)(Info.MaxDamagePercentageStep * Math.Abs(CurrentTemperature / MaxTemperature) * (long)health.MaxHP / 100);

			self.InflictDamage(self, new Damage(damage, damageTypes));
		}

		void INotifyDamage.Damaged(Actor self, AttackInfo e)
		{
			if (IsTraitDisabled)
				return;

			if (e.Attacker == self)
				return;

			if (e.Damage.Value != 0 && (!Info.ChangeTemperatureDamageTypes.IsEmpty && e.Damage.DamageTypes.Overlaps(Info.ChangeTemperatureDamageTypes)))
				ChangeTemperature(self, e.Damage.Value);
			else return;

			if (ticks < Info.DamageRegenDelay)
				ticks = Info.DamageRegenDelay;
		}

		float ISelectionBar.GetValue()
		{
			if (IsTraitDisabled || !Info.ShowSelectionBar || CurrentTemperature == 0)
				return 0;

			var selected = self.World.Selection.Contains(self);
			var rollover = self.World.Selection.RolloverContains(self);
			var regularWorld = self.World.Type == WorldType.Regular;
			var statusBars = Game.Settings.Game.StatusBars;

			var displayHealth = selected || rollover || (regularWorld && statusBars == StatusBarsType.AlwaysShow)
				|| (regularWorld && statusBars == StatusBarsType.DamageShow);

			if (!displayHealth)
				return 0;

			return (float)Math.Abs(CurrentTemperature / MaxTemperature);
		}

		bool ISelectionBar.DisplayWhenEmpty { get { return false; } }

		Color ISelectionBar.GetColor() { return Info.SelectionBarColor; }

		int IDamageModifier.GetDamageModifier(Actor attacker, Damage damage)
		{
			if (IsTraitDisabled || CurrentTemperature == 0 || Info.MaxDamageModifier == 0)
				return 100;
			else
				return 100 + Math.Abs(CurrentTemperature / MaxTemperature) * Info.MaxDamageModifier;
		}

		int ISpeedModifier.GetSpeedModifier()
		{
			if (IsTraitDisabled || CurrentTemperature == 0 || Info.MaxSpeedModifier == 0)
				return 100;
			else
				return 100 + (CurrentTemperature / MaxTemperature) * Info.MaxSpeedModifier;
		}

		int ITurnSpeedModifier.GetTurnSpeedModifier()
		{
			if (IsTraitDisabled || CurrentTemperature == 0 || Info.MaxSpeedModifier == 0)
				return 100;
			else
				return 100 + (CurrentTemperature / MaxTemperature) * Info.MaxSpeedModifier;
		}

		int ITurretTurnSpeedModifier.GetTurretTurnSpeedModifier(string turretName)
		{
			if (IsTraitDisabled || CurrentTemperature == 0 || Info.MaxSpeedModifier == 0)
				return 100;
			else
				return 100 + (CurrentTemperature / MaxTemperature) * Info.MaxSpeedModifier;
		}

		IEnumerable<IRenderable> IRenderModifier.ModifyRender(Actor self, WorldRenderer wr, IEnumerable<IRenderable> r)
		{
			if (IsTraitDisabled || CurrentTemperature == 0)
				return r;

			return ModifiedRender(r);
		}

		IEnumerable<IRenderable> ModifiedRender(IEnumerable<IRenderable> r)
		{
			var alphaRate = CurrentTemperature / MaxTemperature;
			foreach (var a in r)
			{
				yield return a;

				if (!a.IsDecoration && a is IModifyableRenderable ma)
					if (CurrentTemperature > 0)
						yield return ma.WithTint(warmTint, ma.TintModifiers | TintModifiers.ReplaceColor).WithAlpha(alphaRate * warmAlpha);
					else
						yield return ma.WithTint(coolTint, ma.TintModifiers | TintModifiers.ReplaceColor).WithAlpha(alphaRate * coolAlpha);
			}
		}

		IEnumerable<Rectangle> IRenderModifier.ModifyScreenBounds(Actor self, WorldRenderer wr, IEnumerable<Rectangle> bounds)
		{
			return bounds;
		}

		protected override void TraitEnabled(Actor self)
		{
			ticks = Info.InitialRegenDelay;
			CurrentTemperature = 0;
		}

		protected override void TraitDisabled(Actor self)
		{
			if (conditionToken == Actor.InvalidConditionToken)
				return;

			conditionToken = self.RevokeCondition(conditionToken);
		}
	}
}
