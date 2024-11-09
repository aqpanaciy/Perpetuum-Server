using System.Collections.Generic;
using System.Transactions;
using Perpetuum.Data;
using Perpetuum.EntityFramework;
using Perpetuum.Host.Requests;
using Perpetuum.Items;
using Perpetuum.Modules;
using Perpetuum.Robots;
using Perpetuum.Zones;

namespace Perpetuum.RequestHandlers.Zone.Containers
{
    public class EquipModule : ZoneContainerRequestHandler
    {
        private readonly IEntityRepository _entityRepository;

        public EquipModule(IEntityRepository entityRepository)
        {
            _entityRepository = entityRepository;
        }

        public override void HandleRequest(IZoneRequest request)
        {
            using (var scope = Db.CreateTransaction())
            {
                var character = request.Session.Character;
                var player = request.Zone.GetPlayerOrThrow(character);

                CheckPvpState(player).ThrowIfError();
                CheckCombatState(player).ThrowIfError();
                CheckActiveModules(player).ThrowIfError();

                var containerEid = request.Data.GetOrDefault<long>(k.containerEID);
                var container = request.Zone.FindContainerOrThrow(player, containerEid);

                CheckContainerType(container).ThrowIfError();
                CheckFieldTerminalRange(player, container).ThrowIfError();

                player.EnlistTransaction();
                container.EnlistTransaction();

                Transaction.Current.OnCompleted(completed =>
                {
                    player.Initialize(character);
                    player.SendRefreshUnitPacket();

                    var result = new Dictionary<string, object>
                    {
                        {k.robot, player.ToDictionary()}, 
                        {k.container, container.ToDictionary()}
                    };
                    Message.Builder.FromRequest(request).WithData(result).WrapToResult().Send();
                });

                var componentType = request.Data.GetOrDefault<string>(k.robotComponent).ToEnum<RobotComponentType>();
                var component = player.GetRobotComponentOrThrow(componentType);
                var slot = request.Data.GetOrDefault<int>(k.slot);
                component.MakeSlotFree(slot, container);

                var moduleEid = request.Data.GetOrDefault<long>(k.moduleEID);
                var module = (Module)container.GetItemOrThrow(moduleEid).Unstack(1);
                component.EquipModuleOrThrow(module, slot);

                player.Initialize(character);
                player.CheckEnergySystemAndThrowIfFailed();

                player.Save();
                container.Save();
                
                scope.Complete();
            }
        }
    }
}