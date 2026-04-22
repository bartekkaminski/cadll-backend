using System;
using System.Collections.Generic;
using System.Linq;
using ZwSoft.ZwCAD.ApplicationServices;
using ZwSoft.ZwCAD.Colors;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.EditorInput;
using ZwSoft.ZwCAD.Geometry;
using ZwSoft.ZwCAD.GraphicsInterface;
using ZwSoft.ZwCAD.Runtime;

namespace cadll.Templates.Zwcad
{
    public class Main
    {
        // Przykładowa funkcja do obliczania długości linii i polilinii wg koloru
        [CommandMethod("PrzykladowaFunkcjaLiczKolory")]
        public static void CalcLengthsByColor()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            PromptSelectionOptions opts = new PromptSelectionOptions();
            opts.MessageForAdding = "\nWskaż elementy (linie lub polilinie):";
            PromptSelectionResult selRes = ed.GetSelection(opts);

            if (selRes.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\nNie wybrano elementów.");
                return;
            }

            // Klucz: (ColorIndex, DisplayName lub #Index)
            var lengthByColor = new Dictionary<int, (string displayName, double length)>();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject selObj in selRes.Value)
                {
                    if (selObj == null) continue;

                    Entity ent = tr.GetObject(selObj.ObjectId, OpenMode.ForRead) as Entity;
                    if (ent == null) continue;

                    double length = 0;

                    if (ent is Line line)
                        length = line.Length;
                    else if (ent is ZwSoft.ZwCAD.DatabaseServices.Polyline pline)
                        length = pline.Length;
                    else
                        continue;

                    ZwSoft.ZwCAD.Colors.Color color = ent.Color;
                    int colorIndex = color.ColorIndex;
                    string colorKey = !string.IsNullOrWhiteSpace(color.ColorNameForDisplay)
                        ? color.ColorNameForDisplay
                        : $"#{color.ColorIndex}";

                    if (lengthByColor.ContainsKey(colorIndex))
                    {
                        var old = lengthByColor[colorIndex];
                        lengthByColor[colorIndex] = (old.displayName, old.length + length);
                    }
                    else
                    {
                        lengthByColor[colorIndex] = (colorKey, length);
                    }
                }
                tr.Commit();
            }

            if (lengthByColor.Count == 0)
            {
                ed.WriteMessage("\nNie znaleziono linii ani polilinii.");
                return;
            }

            ed.WriteMessage("\n--- Zestawienie długości wg koloru ---");
            ed.WriteMessage("\n--- Rysunek jest wykonany w [cm] -----");
            ed.WriteMessage("\n--------------------------------------");
            ed.WriteMessage("\nKolor".PadRight(20) + "Długość [m]".PadLeft(10));
            ed.WriteMessage("\n" + new string('-', 30));

            foreach (var kvp in lengthByColor.OrderBy(kvp => kvp.Key)) // Sortowanie po ColorIndex rosnąco
            {
                string kolor = kvp.Value.displayName.PadRight(20);
                string metry = Math.Round(kvp.Value.length / 100.0, 2).ToString("0.00").PadLeft(10); // z cm na m
                ed.WriteMessage($"\n{kolor}{metry}");
            }
        }
    }
}
