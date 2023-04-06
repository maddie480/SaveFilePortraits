using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Celeste.Mod.SaveFilePortraits {
    public class SaveFilePortraitsModule : EverestModule {
        public class SaveData : EverestModuleSaveData {
            public string Portrait { get; set; } = "portrait_madeline";
            public string Animation { get; set; } = "idle_normal";
        }

        public override Type SaveDataType => typeof(SaveData);
        public SaveData ModSaveData => (SaveData) _SaveData;

        public static List<Tuple<string, string>> ExistingPortraits;

        private static ILHook slotRenderHook;

        private PortraitPicker portraitPicker;

        public override void Load() {
            On.Celeste.GFX.LoadData += onGFXLoadData;
            IL.Celeste.OuiFileSelectSlot.Setup += onOuiFileSelectSetup;
            Everest.Events.FileSelectSlot.OnCreateButtons += onCreateFileSelectSlotButtons;
            IL.Celeste.OuiFileSelectSlot.Update += onFileSelectSlotUpdate;
            On.Celeste.Overworld.End += onOverworldEnd;
            On.Celeste.OuiFileSelect.Enter += onFileSelectEnter;
            On.Celeste.OuiFileSelectSlot.OnNewGameSelected += onFileSelectNewGameSelected;

            slotRenderHook = new ILHook(typeof(OuiFileSelectSlot).GetMethod("orig_Render"), onFileSelectSlotRender);
        }

        public override void Unload() {
            On.Celeste.GFX.LoadData -= onGFXLoadData;
            IL.Celeste.OuiFileSelectSlot.Setup -= onOuiFileSelectSetup;
            Everest.Events.FileSelectSlot.OnCreateButtons -= onCreateFileSelectSlotButtons;
            IL.Celeste.OuiFileSelectSlot.Update -= onFileSelectSlotUpdate;
            On.Celeste.Overworld.End -= onOverworldEnd;
            On.Celeste.OuiFileSelect.Enter -= onFileSelectEnter;
            On.Celeste.OuiFileSelectSlot.OnNewGameSelected -= onFileSelectNewGameSelected;

            slotRenderHook?.Dispose();
            slotRenderHook = null;
        }

        private void onGFXLoadData(On.Celeste.GFX.orig_LoadData orig) {
            orig();

            // go through all loaded portraits in GFX.PortraitsSpriteBank and list the ones following the idle_[something] pattern.
            // [something] should not include any _ either, and should have dimensions that fit in the allocated space (:assertivebaddy: doesn't look that great on save files).
            ExistingPortraits = new List<Tuple<string, string>>();
            foreach (string portrait in GFX.PortraitsSpriteBank.SpriteData.Keys) {
                SpriteData sprite = GFX.PortraitsSpriteBank.SpriteData[portrait];
                foreach (string animation in sprite.Sprite.Animations.Keys) {
                    if (animation.StartsWith("idle_") && !animation.Substring(5).Contains("_")
                        && sprite.Sprite.Animations[animation].Frames[0].Height <= 200 && sprite.Sprite.Animations[animation].Frames[0].Width <= 200) {

                        ExistingPortraits.Add(new Tuple<string, string>(portrait, animation));
                    }
                }
            }

            Logger.Log("SaveFilePortraits", $"Found {ExistingPortraits.Count} portraits to pick from.");
        }

        private void onOuiFileSelectSetup(ILContext il) {
            ILCursor cursor = new ILCursor(il);

            // replace the **sprite** on save data slots when they're loaded in
            cursor.GotoNext(MoveType.After, instr => instr.MatchLdstr("portrait_madeline"));
            cursor.Emit(OpCodes.Ldarg_0);
            cursor.EmitDelegate<Func<string, OuiFileSelectSlot, string>>((orig, self) => {
                if (self.Exists && !self.Corrupted) {
                    DeserializeSaveData(self.FileSlot, ReadSaveData(self.FileSlot));
                    global::Celeste.SaveData.LoadedModSaveDataIndex = int.MinValue;
                    if (GFX.PortraitsSpriteBank.Has(ModSaveData.Portrait)) {
                        return ModSaveData.Portrait;
                    }
                }
                return orig;
            });

            // replace the **animation** on save data slots when they're loaded in
            cursor.GotoNext(MoveType.After, instr => instr.MatchLdstr("idle_normal"));
            cursor.Emit(OpCodes.Ldarg_0);
            cursor.EmitDelegate<Func<string, OuiFileSelectSlot, string>>((orig, self) => {
                if (self.Exists && !self.Corrupted) {
                    if (GFX.PortraitsSpriteBank.Has(ModSaveData.Portrait) && GFX.PortraitsSpriteBank.SpriteData[ModSaveData.Portrait].Sprite.Has(ModSaveData.Animation)) {
                        return ModSaveData.Animation;
                    }
                }
                return orig;
            });
        }

        private void onCreateFileSelectSlotButtons(List<OuiFileSelectSlot.Button> buttons, OuiFileSelectSlot slot, EverestModuleSaveData modSaveData, bool fileExists) {
            // add the portrait picker option to file select slots.
            buttons.Add(portraitPicker = new PortraitPicker(slot, this));
        }

        private void onFileSelectSlotUpdate(ILContext il) {
            ILCursor cursor = new ILCursor(il);

            cursor.GotoNext(MoveType.AfterLabel, instr => instr.MatchRet());

            // I just want to copy paste what OuiFileSelectSlotLevelSetPicker does in OuiFileSelectSlot.Update but that uses half a million private fields
            // so instead of using reflection I'm going to ask for them through IL aaaaa
            cursor.Emit(OpCodes.Ldarg_0);
            cursor.Emit(OpCodes.Ldarg_0);
            cursor.Emit(OpCodes.Ldfld, typeof(OuiFileSelectSlot).GetField("fileSelect", BindingFlags.NonPublic | BindingFlags.Instance));
            cursor.Emit(OpCodes.Ldarg_0);
            cursor.Emit(OpCodes.Ldfld, typeof(OuiFileSelectSlot).GetField("buttons", BindingFlags.NonPublic | BindingFlags.Instance));
            cursor.Emit(OpCodes.Ldarg_0);
            cursor.Emit(OpCodes.Ldfld, typeof(OuiFileSelectSlot).GetField("buttonIndex", BindingFlags.NonPublic | BindingFlags.Instance));
            cursor.Emit(OpCodes.Ldarg_0);
            cursor.Emit(OpCodes.Ldfld, typeof(OuiFileSelectSlot).GetField("tween", BindingFlags.NonPublic | BindingFlags.Instance));
            cursor.Emit(OpCodes.Ldarg_0);
            cursor.Emit(OpCodes.Ldfld, typeof(OuiFileSelectSlot).GetField("inputDelay", BindingFlags.NonPublic | BindingFlags.Instance));
            cursor.Emit(OpCodes.Ldarg_0);
            cursor.Emit(OpCodes.Callvirt, typeof(OuiFileSelectSlot).GetMethod("get_selected", BindingFlags.NonPublic | BindingFlags.Instance));
            cursor.Emit(OpCodes.Ldarg_0);
            cursor.Emit(OpCodes.Ldfld, typeof(OuiFileSelectSlot).GetField("deleting", BindingFlags.NonPublic | BindingFlags.Instance));

            cursor.EmitDelegate<Action<OuiFileSelectSlot, OuiFileSelect, List<OuiFileSelectSlot.Button>, int, Tween, float, bool, bool>>((self, fileSelect, buttons, buttonIndex,
                tween, inputDelay, selected, deleting) => {
                    if (portraitPicker != null && selected && fileSelect.Selected && fileSelect.Focused &&
                        !self.StartingGame && tween == null && inputDelay <= 0f && !deleting) {

                        // currently highlighted option is the portrait picker, call its Update() method to handle Left and Right presses.
                        portraitPicker.Update(buttons[buttonIndex] == portraitPicker);
                    }
                });
        }

        private void onFileSelectSlotRender(ILContext il) {
            ILCursor cursor = new ILCursor(il);

            // Jump just before rendering the "deleting" screen to make sure we're behind it.
            cursor.GotoNext(MoveType.AfterLabel,
                instr => instr.MatchLdarg(0),
                instr => instr.MatchLdfld<OuiFileSelectSlot>("deletingEase"),
                instr => instr.MatchLdcR4(0f));

            // get half a million private fields
            cursor.Emit(OpCodes.Ldarg_0);

            cursor.Emit(OpCodes.Ldarg_0);
            cursor.Emit(OpCodes.Ldfld, typeof(OuiFileSelectSlot).GetField("selectedEase", BindingFlags.NonPublic | BindingFlags.Instance));

            cursor.Emit(OpCodes.Ldarg_0);
            cursor.Emit(OpCodes.Ldfld, typeof(OuiFileSelectSlot).GetField("buttons", BindingFlags.NonPublic | BindingFlags.Instance));

            cursor.Emit(OpCodes.Ldarg_0);
            cursor.Emit(OpCodes.Ldfld, typeof(OuiFileSelectSlot).GetField("buttonIndex", BindingFlags.NonPublic | BindingFlags.Instance));

            cursor.Emit(OpCodes.Ldarg_0);
            cursor.Emit(OpCodes.Ldfld, typeof(OuiFileSelectSlot).GetField("deleting", BindingFlags.NonPublic | BindingFlags.Instance));

            cursor.Emit(OpCodes.Ldarg_0);
            cursor.Emit(OpCodes.Ldfld, typeof(OuiFileSelectSlot).GetField("wiggler", BindingFlags.NonPublic | BindingFlags.Instance));

            cursor.EmitDelegate<Action<OuiFileSelectSlot, float, List<OuiFileSelectSlot.Button>, int, bool, Wiggler>>((self, selectedEase, buttons, buttonIndex, deleting, wiggler) => {
                if (selectedEase > 0f) {
                    Vector2 position = self.Position + new Vector2(0f, -150f + 350f * selectedEase);
                    float lineHeight = ActiveFont.LineHeight;

                    // go through all buttons, looking for the portrait picker.
                    for (int i = 0; i < buttons.Count; i++) {
                        OuiFileSelectSlot.Button button = buttons[i];
                        if (button == portraitPicker) {
                            // we found it: call its Render method.
                            portraitPicker.Render(position, buttonIndex == i && !deleting, wiggler.Value * 8f);
                        }
                        position.Y += lineHeight * button.Scale + 15f;
                    }
                }
            });
        }

        private void onOverworldEnd(On.Celeste.Overworld.orig_End orig, Overworld self) {
            orig(self);

            // just a bit of cleanup.
            portraitPicker = null;
        }

        private IEnumerator onFileSelectEnter(On.Celeste.OuiFileSelect.orig_Enter orig, OuiFileSelect self, Oui from) {
            // make sure vanilla portraits are loaded (in case the player played a map with a custom Portraits.xml).
            GFX.PortraitsSpriteBank = new SpriteBank(GFX.Portraits, Path.Combine("Graphics", "Portraits.xml"));

            return orig(self, from);
        }

        private void onFileSelectNewGameSelected(On.Celeste.OuiFileSelectSlot.orig_OnNewGameSelected orig, OuiFileSelectSlot self) {
            // when starting a new game, the mod save data will be reset...
            // but we'd very much like to keep the picked portrait in the save data.

            string portrait = ModSaveData.Portrait;
            string animation = ModSaveData.Animation;

            orig(self);

            ModSaveData.Portrait = portrait;
            ModSaveData.Animation = animation;
        }

        // very very similar to OuiFileSelectSlotLevelSetPicker from Everest.
        private class PortraitPicker : OuiFileSelectSlot.Button {
            private OuiFileSelectSlot selectSlot;
            private SaveFilePortraitsModule module;

            private Vector2 arrowOffset;
            private int lastDirection;
            private int currentIndex;

            public PortraitPicker(OuiFileSelectSlot selectSlot, SaveFilePortraitsModule module) {
                this.selectSlot = selectSlot;
                this.module = module;

                Label = Dialog.Clean("SaveFilePortraits_ChangePortrait");
                Scale = 0.5f;
                Action = () => changePortraitSelection(1);

                currentIndex = ExistingPortraits.IndexOf(new Tuple<string, string>(module.ModSaveData.Portrait, module.ModSaveData.Animation));
                if (currentIndex == -1) {
                    currentIndex = 0;
                }

                arrowOffset = new Vector2(20f + ActiveFont.Measure(Label).X / 2 * Scale, 0f);
            }

            public void Update(bool selected) {
                if (selected) {
                    if (Input.MenuLeft.Pressed) {
                        changePortraitSelection(-1);
                    } else if (Input.MenuRight.Pressed) {
                        changePortraitSelection(1);
                    }
                } else {
                    lastDirection = 0;
                }
            }

            public void Render(Vector2 position, bool currentlySelected, float wigglerOffset) {
                Vector2 wigglerShift = Vector2.UnitX * (currentlySelected ? wigglerOffset : 0f);
                Color color = selectSlot.SelectionColor(currentlySelected);

                Vector2 leftArrowWigglerShift = lastDirection <= 0 ? wigglerShift : Vector2.Zero;
                Vector2 rightArrowWigglerShift = lastDirection >= 0 ? wigglerShift : Vector2.Zero;

                ActiveFont.DrawOutline("<", position + leftArrowWigglerShift - arrowOffset, new Vector2(0.5f, 0f), Vector2.One * Scale, color, 2f, Color.Black);
                ActiveFont.DrawOutline(">", position + rightArrowWigglerShift + arrowOffset, new Vector2(0.5f, 0f), Vector2.One * Scale, color, 2f, Color.Black);
            }

            private void changePortraitSelection(int direction) {
                lastDirection = direction;
                Audio.Play((direction > 0) ? "event:/ui/main/button_toggle_on" : "event:/ui/main/button_toggle_off");

                currentIndex += direction;

                // handle overflow
                if (currentIndex >= ExistingPortraits.Count)
                    currentIndex = 0;
                if (currentIndex < 0)
                    currentIndex = ExistingPortraits.Count - 1;

                // commit the change to save data
                module.ModSaveData.Portrait = ExistingPortraits[currentIndex].Item1;
                module.ModSaveData.Animation = ExistingPortraits[currentIndex].Item2;

                // apply the change live
                GFX.PortraitsSpriteBank.CreateOn(selectSlot.Portrait, module.ModSaveData.Portrait);
                selectSlot.Portrait.Play(module.ModSaveData.Animation);
                selectSlot.Portrait.Scale = Vector2.One * (200f / GFX.PortraitsSpriteBank.SpriteData[module.ModSaveData.Portrait].Sources[0].XML.AttrInt("size", 160));

                // save the change to disk if the file already exists (if we are not creating one)
                if (selectSlot.Exists) {
                    module.WriteSaveData(selectSlot.FileSlot, module.SerializeSaveData(selectSlot.FileSlot));
                }

                selectSlot.WiggleMenu();
            }
        }
    }
}
