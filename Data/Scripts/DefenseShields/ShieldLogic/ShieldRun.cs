using VRage.Utils;

namespace DefenseShields
{
    using System;
    using Support;
    using Sandbox.Common.ObjectBuilders;
    using Sandbox.Game.Entities;
    using Sandbox.ModAPI;
    using VRage.Game.Components;
    using VRage.ModAPI;
    using VRage.ObjectBuilders;

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_UpgradeModule), false, "DSControlLarge", "DSControlSmall", "DSControlTable", "NPCControlSB", "NPCControlLB")]
    public partial class DefenseShields : MyGameLogicComponent
    {
        #region Simulation
        public override void OnAddedToContainer()
        {
            if (!_containerInited)
            {
                PowerPreInit();
                NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
                NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME;

                _containerInited = true;
            }

            if (Entity.InScene) OnAddedToScene();
        }

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);
            MaxString = Localization.GetText("InfoShieldMaxString");
            CapString = Localization.GetText("InfoShieldCapString");
            Shield = (IMyUpgradeModule)Entity;
            SuppressHex = Shield.BlockDefinition.SubtypeName.Contains("GFA");
            MyCube = (MyCubeBlock)Entity;
            StorageSetup();
        }

        public override void OnAddedToScene()
        {

            if (Session.Enforced.Debug == 3) Log.Line($"OnAddedToScene: GridId:{Shield.CubeGrid.EntityId} - ShieldId [{Shield.EntityId}]");

            MyGrid = MyCube?.CubeGrid;
            RegisterEvents();
            AssignSlots();
            _resetEntity = true;

        }

        public override void UpdateOnceBeforeFrame()
        {
            base.UpdateOnceBeforeFrame();
            if (!_bInit) BeforeInit();
            else if (_bCount < SyncCount * _bTime)
            {
                NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
                if (ShieldComp?.DefenseShields != null && ShieldComp.DefenseShields.Warming) _bCount++;
            }
            else _readyToSync = true;
        }

        public override void UpdateBeforeSimulation()
        {
            try
            {
                ShieldRun();
                if (StateChangeRequest)
                    ShieldChangeState();
            }
            catch (Exception ex) { Log.Line($"Exception in UpdateBeforeSimulation: {ex}"); }
        }

        public override bool IsSerialized()
        {
            if (MyAPIGateway.Multiplayer.IsServer)
            {
                if (Shield.Storage != null)
                {
                    DsState.SaveState();
                    DsSet.SaveSettings();
                }
            }
            return false;
        }

        public override void OnBeforeRemovedFromContainer()
        {
            if (Entity.InScene) OnRemovedFromScene();
        }

        public override void OnRemovedFromScene()
        {
            if (!_allInited) return;
            if (Session.Enforced.Debug >= 3) Log.Line($"OnRemovedFromScene: {ShieldMode} - GridId:{Shield.CubeGrid.EntityId} - ShieldId [{Shield.EntityId}]");

            if (ShieldComp?.DefenseShields == this)
            {
                OfflineShield(true, false, true);
                ShieldComp.DefenseShields = null;
            }

            RegisterEvents(false);
            InitEntities(false);
            _shellPassive?.Render?.RemoveRenderObjects();
            ShellActive?.Render?.RemoveRenderObjects();
            ShieldEnt?.Render?.RemoveRenderObjects();
        }

        public override void MarkForClose()
        {
            base.MarkForClose();
        }

        public override void Close()
        {
            base.Close();
            if (!_allInited) return;
            if (Session.Enforced.Debug >= 3) Log.Line($"Close: {ShieldMode} - ShieldId [{Shield.EntityId}]");

            if (ShieldComp?.DefenseShields == this)
            {
                OfflineShield(true, false, true);
                ShieldComp.DefenseShields = null;
            }

            if (Session.Instance.Controllers.Contains(this)) Session.Instance.Controllers.Remove(this);
            bool value1;

            if (Session.Instance.FunctionalShields.ContainsKey(this)) Session.Instance.FunctionalShields.TryRemove(this, out value1);

            Icosphere = null;
            InitEntities(false);
            MyAPIGateway.Session.OxygenProviderSystem.RemoveOxygenGenerator(_ellipsoidOxyProvider);
            if (_sink != null)
            {
                _resourceInfo = new MyResourceSinkInfo
                {
                    ResourceTypeId = GId,
                    MaxRequiredInput = 0f,
                    RequiredInputFunc = null
                };
                _sink.Init(MyStringHash.GetOrCompute("Defense"), _resourceInfo);
                _sink = null;
            }

            ShieldComp = null;

        }
        #endregion
    }
}