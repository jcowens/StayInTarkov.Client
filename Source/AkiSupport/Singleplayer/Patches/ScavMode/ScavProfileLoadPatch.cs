using System;
using Comfort.Common;
using EFT;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using StayInTarkov.AkiSupport.Singleplayer.Utils;

namespace StayInTarkov.AkiSupport.Singleplayer.Patches.ScavMode
{
    public class ScavProfileLoadPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            var desiredType = typeof(TarkovApplication.Struct302);

            var desiredMethod = desiredType.GetMethods(StayInTarkovHelperConstants.PrivateFlags)
                .FirstOrDefault(x => x.Name == "MoveNext");

            Logger.LogDebug($"{this.GetType().Name} Type: {desiredType?.Name}");
            Logger.LogDebug($"{this.GetType().Name} Method: {desiredMethod?.Name}");

            return desiredMethod;
        }

        [PatchTranspiler]
        private static IEnumerable<CodeInstruction> PatchTranspile(ILGenerator generator, IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);

            // Search for code where backend.Session.getProfile() is called.
            var searchCode = new CodeInstruction(OpCodes.Callvirt, AccessTools.Method(StayInTarkovHelperConstants.BackendSessionInterfaceType, "get_Profile"));
            var searchIndex = -1;

            for (var i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode == searchCode.opcode && codes[i].operand == searchCode.operand)
                {
                    searchIndex = i;
                    break;
                }
            }

            // Patch failed.
            if (searchIndex == -1)
            {
                Logger.LogError($"Patch {MethodBase.GetCurrentMethod()} failed: Could not find reference code.");
                return instructions;
            }

            // Move back by 2. This is the start of this method call.
            searchIndex -= 2;

            var brFalseLabel = generator.DefineLabel();
            var brLabel = generator.DefineLabel();
            var newCodes = CodeGenerator.GenerateInstructions(new List<Code>()
            {
                new Code(OpCodes.Ldloc_1),
                new Code(OpCodes.Call, typeof(ClientApplication<ISession>), "get_Session"),
                new Code(OpCodes.Ldloc_1),
                new Code(OpCodes.Ldfld, typeof(TarkovApplication), "_raidSettings"),
                new Code(OpCodes.Callvirt, typeof(RaidSettings), "get_IsPmc"),
                new Code(OpCodes.Brfalse, brFalseLabel),
                new Code(OpCodes.Callvirt, StayInTarkovHelperConstants.BackendSessionInterfaceType, "get_Profile"),
                new Code(OpCodes.Br, brLabel),
                new CodeWithLabel(OpCodes.Callvirt, brFalseLabel, StayInTarkovHelperConstants.SessionInterfaceType, "get_ProfileOfPet"),
                new CodeWithLabel(OpCodes.Stfld, brLabel, typeof(TarkovApplication).GetNestedTypes(BindingFlags.Public).Single(IsTargetNestedType), "profile")
            });

            codes.RemoveRange(searchIndex, 4);
            codes.InsertRange(searchIndex, newCodes);

            return codes.AsEnumerable();
        }

        private static bool IsTargetNestedType(System.Type nestedType)
        {
            return nestedType.GetMethods(StayInTarkovHelperConstants.PrivateFlags)
                .Count(x => x.GetParameters().Length == 1 && x.GetParameters()[0].ParameterType == typeof(IResult)) > 0 && nestedType.GetField("savageProfile") != null;
        }
    }
}
