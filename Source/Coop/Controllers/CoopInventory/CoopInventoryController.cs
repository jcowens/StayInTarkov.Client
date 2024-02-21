﻿using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using JetBrains.Annotations;
using StayInTarkov.Coop.NetworkPacket;
using StayInTarkov.Networking;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace StayInTarkov.Coop.Controllers.CoopInventory
{
    public class CoopInventoryController
        // At this point in time. PlayerOwnerInventoryController is required to fix Malfunction and Discard errors. This class needs to be replaced with PlayerInventoryController.
        : EFT.Player.PlayerOwnerInventoryController, ICoopInventoryController
    {
        ManualLogSource BepInLogger { get; set; }

        public HashSet<string> AlreadySent = new();

        private EFT.Player Player { get; set; }

        private HashSet<AbstractInventoryOperation> InventoryOperations { get; } = new();

        public override void Execute(AbstractInventoryOperation operation, [CanBeNull] Callback callback)
        {
            // If operation created via this player, then play out that operation
            if (InventoryOperations.Any(x => x.Id == operation.Id))
            {
                base.Execute(InventoryOperations.First(x => x.Id == operation.Id), callback);
                return;
            }

            // Debug the operation
            // 
            BepInLogger.LogDebug($"{operation}");

            // Create the packet to send to the Server
            using MemoryStream memoryStream = new();
            using (BinaryWriter binaryWriter = new(memoryStream))
            {
                var desc = OperationToDescriptorHelpers.FromInventoryOperation(operation, false, false);
                binaryWriter.WritePolymorph(desc);
                var opBytes = memoryStream.ToArray();

                var itemId = "";
                var templateId = "";
                if (operation is MoveInternalOperation moveOperation) 
                {
                    itemId = moveOperation.Item.Id;
                    templateId = moveOperation.Item.TemplateId;
                }
                //if (operation is MoveInternalOperation1 otherOperation)
                //{
                //    itemId = otherOperation.Item.Id;
                //    templateId = otherOperation.Item.TemplateId;
                //}
                //if (operation is MoveInternalOperation2 throwOperation)
                //{
                //    itemId = throwOperation.Item.Id;
                //    templateId = throwOperation.Item.TemplateId;
                //}

                ItemPlayerPacket itemPlayerPacket = new ItemPlayerPacket(Player.ProfileId, itemId, templateId, "PolymorphInventoryOperation");
                itemPlayerPacket.OperationBytes = opBytes;
                itemPlayerPacket.CallbackId = operation.Id;
                itemPlayerPacket.InventoryId = this.ID;

                BepInLogger.LogDebug($"Operation: {operation.GetType().Name}, IC Name: {this.Name}, {Player.name}");


                ReflectionHelpers.SetFieldOrPropertyFromInstance<CommandStatus>(operation, "commandStatus_0", CommandStatus.Begin);

                var s = itemPlayerPacket.Serialize();
                GameClient.SendData(s);
                InventoryOperations.Add(operation);
            }

            if (!vmethod_0(operation))
            {
                operation.Dispose();
                callback?.Fail($"Can't execute {operation}", 1);
                return;
            }
            else
            {
                callback?.Succeed();
                //operation.vmethod_0(delegate (IResult result)
                //{
                //    if (!result.Succeed)
                //    {
                //        Logger.LogError("[{0}][{5}] {1} - Local operation failed: {2} - {3}\r\nError: {4}", Time.frameCount, ID, operation.Id, operation, result.Error, Name);
                //    }
                //    callback?.Invoke(result);
                //});
            }
        }

        public void ReceiveExecute(AbstractInventoryOperation operation, string packetJson)
        {
            //BepInLogger.LogInfo($"ReceiveExecute");
            //BepInLogger.LogInfo($"{packetJson}");

            if (operation == null)
                return;

            BepInLogger.LogDebug($"ReceiveExecute:{operation}");

            var cachedOperation = InventoryOperations.FirstOrDefault(x => x.Id == operation.Id);
            // Operation created via this player
            if (cachedOperation != null)
            {
                cachedOperation.vmethod_0((executeResult) =>
                {

                    //BepInLogger.LogInfo($"operation.vmethod_0 : {executeResult}");
                    if (executeResult.Succeed)
                    {
                        RaiseInvEvents(cachedOperation, CommandStatus.Succeed);
                        RaiseInvEvents(operation, CommandStatus.Succeed);
                    }
                    else
                    {
                        RaiseInvEvents(cachedOperation, CommandStatus.Failed);
                        RaiseInvEvents(operation, CommandStatus.Failed);
                    }
                    cachedOperation.Dispose();


                }, false);
            }
            else
            {
                // Operation created by another player
                base.Execute(operation, (result) => { });
            }
        }

        void RaiseInvEvents(object operation, CommandStatus status)
        {
            if (operation == null)
                return;

            ReflectionHelpers.SetFieldOrPropertyFromInstance<CommandStatus>(operation, "commandStatus_0", status);
        }

        public void CancelExecute(uint id)
        {
            BepInLogger.LogError($"CancelExecute");
            BepInLogger.LogError($"OperationId:{id}");
            // If operation created via this player, then cancel that operation
            var operation = InventoryOperations.FirstOrDefault(x => x.Id == id);
            if(operation != null)
            {
                operation.vmethod_0(delegate (IResult result)
                {
                    ReflectionHelpers.SetFieldOrPropertyFromInstance<CommandStatus>(operation, "commandStatus_0", CommandStatus.Failed);
                });
            }
        }

        public override void OutProcess(TraderControllerClass executor, Item item, ItemAddress from, ItemAddress to, IOperation1 operation, Callback callback)
        {
            base.OutProcess(executor, item, from, to, operation, callback);
        }

        public override void InProcess(TraderControllerClass executor, Item item, ItemAddress to, bool succeed, IOperation1 operation, Callback callback)
        {
            // Taken from EFT.Player.PlayerInventoryController
            if (!succeed)
            {
                callback.Succeed();
                return;
            }
            base.InProcess(executor, item, to, succeed, operation, callback);
        }

        public CoopInventoryController(EFT.Player player, Profile profile, bool examined) : base(player, profile, examined)
        {
            BepInLogger = BepInEx.Logging.Logger.CreateLogSource(nameof(CoopInventoryController));
            Player = player;
            /*if (player.Side != EPlayerSide.Savage && !IsDiscardLimitsFine(DiscardLimits))
                ResetDiscardLimits();*/
        }

        public override Task<IResult> LoadMagazine(BulletClass sourceAmmo, MagazineClass magazine, int loadCount, bool ignoreRestrictions)
        {
            BepInLogger.LogDebug("LoadMagazine");
            BepInLogger.LogDebug($"{sourceAmmo}:{magazine}:{loadCount}:{ignoreRestrictions}");
            return base.LoadMagazine(sourceAmmo, magazine, loadCount, ignoreRestrictions);
        }

        public override async Task<IResult> UnloadMagazine(MagazineClass magazine)
        {
            BepInLogger.LogDebug($"Starting UnloadMagazine for magazine {magazine.Id}");
            int retryCount = 3;
            int delayBetweenRetries = 500;

            while (retryCount-- > 0)
            {
                try
                {
                    IResult result = await base.UnloadMagazine(magazine);
                    if (result.Failed)
                    {
                        BepInLogger.LogError($"Failed to unload magazine {magazine.Id}: {result.Error}");
                        if (retryCount > 0) await Task.Delay(delayBetweenRetries);
                        else return result;
                    }
                    else
                    {
                        BepInLogger.LogDebug($"Successfully unloaded magazine {magazine.Id}");
                        return SuccessfulResult.New;
                    }
                }
                catch (Exception ex)
                {
                    BepInLogger.LogError($"Exception in UnloadMagazine for magazine {magazine.Id}: {ex.Message}");
                    if (retryCount <= 0) return new FailedResult($"Exception occurred: {ex.Message}", -1);
                    await Task.Delay(delayBetweenRetries);
                }
            }
            return new FailedResult("Failed to unload magazine after multiple attempts.", -1);
        }

        //public void ReceiveUnloadMagazineFromServer(ItemPlayerPacket unloadMagazinePacket)
        //{
        //    BepInLogger.LogInfo("ReceiveUnloadMagazineFromServer");
        //    if (ItemFinder.TryFindItem(unloadMagazinePacket.ItemId, out Item magazine))
        //    {
        //        base.UnloadMagazine((MagazineClass)magazine);

        //    }
        //}

        public override void ThrowItem(Item item, IEnumerable<ItemsCount> destroyedItems, Callback callback = null, bool downDirection = false)
        {
            base.ThrowItem(item, destroyedItems, callback, downDirection);
        }

        public static bool IsDiscardLimitsFine(Dictionary<string, int> DiscardLimits)
        {
            return DiscardLimits != null
                && DiscardLimits.Count > 0
                && DiscardLimits.ContainsKey("5449016a4bdc2d6f028b456f") // Roubles, Value: 20000
                && DiscardLimits.ContainsKey("5696686a4bdc2da3298b456a") // Dollars, Value: 0
                && DiscardLimits.ContainsKey("569668774bdc2da2298b4568") // Euros, Value: 0
                && DiscardLimits.ContainsKey("5448be9a4bdc2dfd2f8b456a") // RGD-5 Grenade, Value: 20
                && DiscardLimits.ContainsKey("5710c24ad2720bc3458b45a3") // F-1 Grenade, Value: 20
                && DiscardLimits.ContainsKey(DogtagComponent.BearDogtagsTemplate) // Value: 0
                && DiscardLimits.ContainsKey(DogtagComponent.UsecDogtagsTemplate); // Value: 0
        }

        // PlayerOwnerInventoryController methods. We should inherit EFT.Player.PlayerInventoryController and override these methods based on EFT.Player.PlayerOwnerInventoryController

        public override void CallMalfunctionRepaired(Weapon weapon)
        {
            base.CallMalfunctionRepaired(weapon);
            if (!Player.IsAI && (bool)Singleton<SettingsManager>.Instance.Game.Settings.MalfunctionVisability)
            {
                MonoBehaviourSingleton<PreloaderUI>.Instance.MalfunctionGlow.ShowGlow(BattleUIMalfunctionGlow.GlowType.Repaired, force: true, GetMalfunctionGlowAlphaMultiplier());
            }
        }

        private float GetMalfunctionGlowAlphaMultiplier()
        {
            float result = 0.5f;
            if (Player.HealthController.FindActiveEffect<IEffect21>() != null)
            {
                result = 1f;
            }
            return result;
        }
    }


}
