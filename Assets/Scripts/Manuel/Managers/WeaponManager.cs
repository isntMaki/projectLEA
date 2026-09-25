using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectLEA.Manuel.Managers
{
    /// <summary>
    /// Owns the weapon catalogue, what the player has bought, and what is equipped.
    ///
    /// The catalogue is authored in the Inspector: add entries, set each one's category,
    /// and only the fields that category actually uses will be shown.
    ///
    /// BUY AND SELL: the model is one carried weapon at a time. Buying a weapon sells the
    /// currently equipped one back first, and the refund counts toward the purchase. So with
    /// 800 credits you can buy an 800 weapon, then still switch to a 500 one, because the
    /// 800 comes back when the first is sold. If the refund plus the balance still does not
    /// cover the new weapon, the purchase is refused.
    /// </summary>
    public class WeaponManager : ManagerBase<WeaponManager>
    {
        [Tooltip("Every weapon in the game. Add entries here.")]
        [SerializeField] private List<WeaponDefinition> catalog = new List<WeaponDefinition>();

        [Tooltip("Fill the catalogue with throwaway sample weapons when it is empty, purely so " +
                 "the loadout screen has something to show. Stops the moment you add a real entry.")]
        [SerializeField] private bool seedTestCatalogueWhenEmpty = true;

        [Range(0f, 1f)]
        [Tooltip("Fraction of the purchase price returned when a weapon is sold. 1 = full refund. " +
                 "Lower it if you want swapping to cost something.")]
        [SerializeField] private float sellRefundRatio = 1f;

        private readonly List<int> _owned = new List<int>();

        /// <summary>Index into <see cref="Catalog"/>, or -1 when nothing is equipped.</summary>
        public int EquippedIndex { get; private set; } = -1;

        public IReadOnlyList<WeaponDefinition> Catalog => catalog;

        public float SellRefundRatio => sellRefundRatio;

        /// <summary>Raised after a successful purchase, with what it cost.</summary>
        public event Action<int, WeaponDefinition, int> OnWeaponPurchased;

        /// <summary>Raised after a successful sale, with the amount refunded.</summary>
        public event Action<int, WeaponDefinition, int> OnWeaponSold;

        /// <summary>Raised whenever the equipped weapon changes.</summary>
        public event Action<int, WeaponDefinition> OnWeaponEquipped;

        public WeaponDefinition Equipped =>
            IsValidIndex(EquippedIndex) ? catalog[EquippedIndex] : null;

        protected override void OnManagerAwake()
        {
            if (catalog.Count == 0 && seedTestCatalogueWhenEmpty)
            {
                SeedTestCatalogue();
                Debug.LogWarning("[WeaponManager] The catalogue was empty, so it was filled with " +
                                 "throwaway test weapons just so the loadout screen has something to " +
                                 "show. They vanish as soon as you add a real entry in the Inspector.");
            }

            RebuildOwnedFromDefaults();
            EquipDefault();
        }

        // ------------------------------------------------------------------
        // Queries
        // ------------------------------------------------------------------
        public bool IsValidIndex(int index) => index >= 0 && index < catalog.Count;

        public WeaponDefinition Get(int index) => IsValidIndex(index) ? catalog[index] : null;

        public bool IsOwned(int index) => _owned.Contains(index);

        public bool IsEquipped(int index) => EquippedIndex == index;

        /// <summary>What selling this weapon would return right now. 0 if it is not owned.</summary>
        public int GetSellValue(int index)
        {
            var weapon = Get(index);
            if (weapon == null || !IsOwned(index)) return 0;

            return Mathf.RoundToInt(weapon.cost * Mathf.Clamp01(sellRefundRatio));
        }

        /// <summary>True when the currently picked class is allowed to use this weapon.</summary>
        public bool IsAllowedByClass(int index)
        {
            var weapon = Get(index);
            if (weapon == null) return false;

            if (!ClassManager.Exists || !ClassManager.Instance.HasSelection) return true;
            return ClassManager.Instance.Selected.AllowsWeapon(weapon);
        }

        /// <summary>
        /// What the balance would become if this weapon were bought right now, including the
        /// refund from selling whatever is equipped. Negative means it is out of reach.
        /// </summary>
        public int ProjectedBalanceAfterBuying(int index)
        {
            var weapon = Get(index);
            if (weapon == null) return EconomyManager.Exists ? EconomyManager.Instance.Money : 0;

            int money = EconomyManager.Exists ? EconomyManager.Instance.Money : 0;

            if (IsOwned(index)) return money;

            // The equipped weapon is sold to help pay for the new one.
            if (IsValidIndex(EquippedIndex) && IsOwned(EquippedIndex))
                money += GetSellValue(EquippedIndex);

            return money - weapon.cost;
        }

        public bool CanBuy(int index)
        {
            if (!IsValidIndex(index)) return false;
            if (IsOwned(index)) return true;
            if (!IsAllowedByClass(index)) return false;

            return ProjectedBalanceAfterBuying(index) >= 0;
        }

        // ------------------------------------------------------------------
        // Buying and selling
        // ------------------------------------------------------------------
        /// <summary>
        /// The loadout screen's single action. Buys the weapon if needed - selling the
        /// currently equipped one to help pay for it - then equips it.
        /// </summary>
        public bool TrySelect(int index)
        {
            var weapon = Get(index);
            if (weapon == null) return false;

            if (!IsAllowedByClass(index))
            {
                Debug.Log($"[WeaponManager] '{weapon.displayName}' is not available to the current class.");
                return false;
            }

            if (!IsOwned(index) && !Purchase(index)) return false;

            return TryEquip(index);
        }

        /// <summary>
        /// Buys a weapon, selling the equipped one first if that is what makes it affordable.
        /// </summary>
        public bool Purchase(int index)
        {
            var weapon = Get(index);
            if (weapon == null || IsOwned(index)) return false;

            if (!IsAllowedByClass(index)) return false;

            int money = EconomyManager.Exists ? EconomyManager.Instance.Money : 0;

            // Sell the carried weapon to fund the new one. This is what makes swapping work
            // when the balance alone would not cover it.
            int refund = 0;
            if (IsValidIndex(EquippedIndex) && IsOwned(EquippedIndex))
            {
                refund = Sell(EquippedIndex, quiet: true);
            }

            if (money + refund < weapon.cost)
            {
                // Put the old weapon back - the purchase is not happening.
                if (refund > 0) RestoreOwnership(EquippedIndex, refund);

                Debug.Log($"[WeaponManager] Cannot afford '{weapon.displayName}' " +
                          $"({weapon.cost}). Balance {money}, refund {refund}.");
                return false;
            }

            if (EconomyManager.Exists && !EconomyManager.Instance.TrySpend(weapon.cost))
            {
                if (refund > 0) RestoreOwnership(EquippedIndex, refund);
                return false;
            }

            _owned.Add(index);
            OnWeaponPurchased?.Invoke(index, weapon, weapon.cost);

            Debug.Log($"[WeaponManager] Bought '{weapon.displayName}' for {weapon.cost}" +
                      (refund > 0 ? $" (sold the previous weapon for {refund})." : "."));

            return true;
        }

        /// <summary>
        /// Sells an owned weapon and refunds it. Selling the equipped weapon also unequips it.
        /// </summary>
        public bool TrySell(int index)
        {
            if (!IsOwned(index)) return false;
            Sell(index, quiet: false);
            return true;
        }

        private int Sell(int index, bool quiet)
        {
            var weapon = Get(index);
            if (weapon == null || !IsOwned(index)) return 0;

            int refund = GetSellValue(index);

            _owned.Remove(index);
            if (EquippedIndex == index) EquippedIndex = -1;

            if (EconomyManager.Exists && refund > 0) EconomyManager.Instance.Add(refund);

            if (!quiet)
            {
                OnWeaponSold?.Invoke(index, weapon, refund);
                Debug.Log($"[WeaponManager] Sold '{weapon.displayName}' for {refund}.");
            }

            return refund;
        }

        /// <summary>Undoes a sale when the purchase that justified it fell through.</summary>
        private void RestoreOwnership(int index, int refund)
        {
            if (!_owned.Contains(index)) _owned.Add(index);
            if (EconomyManager.Exists && refund > 0) EconomyManager.Instance.Add(-refund);
        }

        public bool TryEquip(int index)
        {
            if (!IsValidIndex(index) || !IsOwned(index)) return false;

            EquippedIndex = index;
            OnWeaponEquipped?.Invoke(index, catalog[index]);
            Debug.Log($"[WeaponManager] Equipped '{catalog[index].displayName}'.");
            return true;
        }

        /// <summary>Drops the equipped weapon. Used when a class is re-picked.</summary>
        public void ClearEquipped()
        {
            EquippedIndex = -1;
        }

        /// <summary>Forgets everything bought, keeping only the defaults. New match.</summary>
        public void ResetPurchases()
        {
            RebuildOwnedFromDefaults();
            EquipDefault();
        }

        /// <summary>
        /// Equips the weapon a fresh round starts with: the sidearm you spawn with, or the
        /// knife if nothing else is carried. Nobody should ever stand in a round with their
        /// hands empty - and EquippedIndex of -1 means WeaponUser has nothing to fire.
        /// </summary>
        private void EquipDefault()
        {
            EquippedIndex = -1;

            int knife = -1;

            for (int i = 0; i < catalog.Count; i++)
            {
                var weapon = catalog[i];
                if (weapon == null || !weapon.ownedByDefault) continue;

                if (weapon.category == WeaponCategory.Melee)
                {
                    knife = i;
                    continue;
                }

                TryEquip(i);
                return;
            }

            if (knife >= 0) TryEquip(knife);
        }

        private void RebuildOwnedFromDefaults()
        {
            _owned.Clear();

            for (int i = 0; i < catalog.Count; i++)
            {
                if (catalog[i] != null && catalog[i].ownedByDefault) _owned.Add(i);
            }
        }

        // ------------------------------------------------------------------
        // Throwaway test content
        // ------------------------------------------------------------------
        /// <summary>
        /// One throwaway weapon per category, so the loadout screen can be judged before any
        /// real content exists. Deliberately not serialized: these only ever exist at runtime,
        /// so they cannot end up saved into the scene by accident.
        /// </summary>
        private void SeedTestCatalogue()
        {
            // ScriptableObject.CreateInstance, not new: these are assets in spirit, just not
            // saved to disk. Real ones are made with Tools > Manuel > Loadout.
            catalog.Add(TestWeapon(w =>
            {
                w.displayName = "Test1 Blade";
                w.category = WeaponCategory.Melee;
                w.description = "Throwaway melee entry - delete once real weapons exist.";
                w.cost = 0;
                w.ownedByDefault = true;
                w.damage = 24f;
                w.delay = 0.55f;
                w.windup = 0.14f;
                w.range = 2.4f;
                w.swingArc = 80f;
            }));

            catalog.Add(TestWeapon(w =>
            {
                w.displayName = "Test2 Pistol";
                w.category = WeaponCategory.Gun;
                w.description = "Throwaway gun entry - delete once real weapons exist.";
                w.cost = 150;
                w.damage = 12f;
                w.delay = 0.28f;
                w.windup = 0.06f;
                w.range = 40f;
                w.projectileSpeed = 60f;
                w.magazineSize = 12;
                w.reserveAmmo = 48;
                w.reloadTime = 1.5f;
            }));

            catalog.Add(TestWeapon(w =>
            {
                w.displayName = "Test3 Bow";
                w.category = WeaponCategory.Bow;
                w.description = "Throwaway bow entry - delete once real weapons exist.";
                w.cost = 200;
                w.damage = 35f;
                w.delay = 0.9f;
                w.windup = 0.35f;
                w.range = 55f;
                w.projectileSpeed = 45f;
                w.magazineSize = 1;
                w.reserveAmmo = 20;
                w.reloadTime = 0.7f;
                w.drawTime = 0.8f;
            }));

            catalog.Add(TestWeapon(w =>
            {
                w.displayName = "Test4 Grenade";
                w.category = WeaponCategory.Thrown;
                w.description = "Throwaway thrown entry - delete once real weapons exist.";
                w.cost = 120;
                w.damage = 45f;
                w.delay = 1.1f;
                w.windup = 0.3f;
                w.range = 25f;
                w.projectileSpeed = 18f;
                w.magazineSize = 1;
                w.reserveAmmo = 3;
                w.reloadTime = 0.6f;
                w.fuse = 2f;
            }));

            catalog.Add(TestWeapon(w =>
            {
                w.displayName = "Test5 Rocket";
                w.category = WeaponCategory.Launcher;
                w.description = "Throwaway launcher entry - delete once real weapons exist.";
                w.cost = 400;
                w.damage = 70f;
                w.delay = 1.6f;
                w.windup = 0.4f;
                w.range = 60f;
                w.projectileSpeed = 30f;
                w.magazineSize = 1;
                w.reserveAmmo = 4;
                w.reloadTime = 2.2f;
                w.splashRadius = 3.5f;
            }));
        }

        /// <summary>Builds an unsaved weapon instance for the throwaway test catalogue.</summary>
        private static WeaponDefinition TestWeapon(System.Action<WeaponDefinition> configure)
        {
            var weapon = ScriptableObject.CreateInstance<WeaponDefinition>();
            configure(weapon);
            weapon.name = weapon.displayName;
            return weapon;
        }
    }
}
