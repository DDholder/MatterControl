﻿/*
Copyright (c) 2014, Lars Brubaker
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.
2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

The views and conclusions contained in the software and documentation are those
of the authors and should not be interpreted as representing official policies,
either expressed or implied, of the FreeBSD Project.
*/

using MatterHackers.Agg;
using MatterHackers.GCodeVisualizer;
using MatterHackers.Localizations;
using MatterHackers.MatterControl.PrinterCommunication;
using MatterHackers.MatterControl.SlicerConfiguration;
using MatterHackers.MeshVisualizer;
using MatterHackers.VectorMath;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MatterHackers.MatterControl.ConfigurationPage.PrintLeveling
{
	public class LevelWizard3x3Mesh : LevelWizardMeshBase
	{
		public LevelWizard3x3Mesh(LevelWizardBase.RuningState runningState)
			: base(runningState, 500, 370, 21, 3, 3)
		{
		}

		public static string ApplyLeveling(string lineBeingSent, Vector3 currentDestination, PrinterMachineInstruction.MovementTypes movementMode)
		{
			var settings = ActiveSliceSettings.Instance;
			if (settings?.GetValue<bool>(SettingsKey.print_leveling_enabled) == true
				&& (lineBeingSent.StartsWith("G0 ") || lineBeingSent.StartsWith("G1 "))
				&& lineBeingSent.Length > 2
				&& lineBeingSent[2] == ' ')
			{
				PrintLevelingData levelingData = ActiveSliceSettings.Instance.Helpers.GetPrintLevelingData();
				return GetLevelingFunctions(3, 3, levelingData)
					.DoApplyLeveling(lineBeingSent, currentDestination, movementMode);
			}

			return lineBeingSent;
		}

		public static List<string> ProcessCommand(string lineBeingSent)
		{
			int commentIndex = lineBeingSent.IndexOf(';');
			if (commentIndex > 0) // there is content in front of the ;
			{
				lineBeingSent = lineBeingSent.Substring(0, commentIndex).Trim();
			}
			List<string> lines = new List<string>();
			lines.Add(lineBeingSent);
			if (lineBeingSent.StartsWith("G28")
				|| lineBeingSent.StartsWith("G29"))
			{
				lines.Add("M114");
			}

			return lines;
		}

		public override Vector2 GetPrintLevelPositionToSample(int index)
		{
			var manualPositions = GetManualPositions(ActiveSliceSettings.Instance.GetValue(SettingsKey.leveling_manual_positions), 9);
			if (manualPositions != null)
			{
				return manualPositions[index];
			}

			Vector2 bedSize = ActiveSliceSettings.Instance.GetValue<Vector2>(SettingsKey.bed_size);
			Vector2 printCenter = ActiveSliceSettings.Instance.GetValue<Vector2>(SettingsKey.print_center);

			if (ActiveSliceSettings.Instance.GetValue<BedShape>(SettingsKey.bed_shape) == BedShape.Circular)
			{
				// reduce the bed size by the ratio of the radius (square root of 2) so that the sample positions will fit on a ciclular bed
				bedSize *= 1.0 / Math.Sqrt(2);
			}

			// we know we are getting 3x3 sample positions they run like this
			// 6 7 8  Y max
			// 3 4 5
			// 0 1 2  Y min
			int xIndex = index % 3;
			int yIndex = index / 3;

			Vector2 samplePosition = new Vector2();
			switch (xIndex)
			{
				case 0:
					samplePosition.x = printCenter.x - (bedSize.x / 2) * .8;
					break;

				case 1:
					samplePosition.x = printCenter.x;
					break;

				case 2:
					samplePosition.x = printCenter.x + (bedSize.x / 2) * .8;
					break;

				default:
					throw new IndexOutOfRangeException();
			}

			switch (yIndex)
			{
				case 0:
					samplePosition.y = printCenter.y - (bedSize.y / 2) * .8;
					break;

				case 1:
					samplePosition.y = printCenter.y;
					break;

				case 2:
					samplePosition.y = printCenter.y + (bedSize.y / 2) * .8;
					break;

				default:
					throw new IndexOutOfRangeException();
			}

			return samplePosition;
		}
	}

	public abstract class LevelWizardMeshBase : LevelWizardBase
	{
		private static MeshLevlingFunctions currentLevelingFunctions = null;
		private LevelingStrings levelingStrings = new LevelingStrings();

		public LevelWizardMeshBase(LevelWizardBase.RuningState runningState, int width, int height, int totalSteps, int gridWidth, int gridHeight)
			: base(width, height, totalSteps)
		{
			string printLevelWizardTitle = "MatterControl";
			string printLevelWizardTitleFull = "Print Leveling Wizard".Localize();
			Title = string.Format("{0} - {1}", printLevelWizardTitle, printLevelWizardTitleFull);
			int probeCount = gridWidth * gridHeight;
			List<ProbePosition> probePositions = new List<ProbePosition>(probeCount);
			for (int i = 0; i < probeCount; i++)
			{
				probePositions.Add(new ProbePosition());
			}

			printLevelWizard = new WizardControl();
			AddChild(printLevelWizard);

			if (runningState == LevelWizardBase.RuningState.InitialStartupCalibration)
			{
				string requiredPageInstructions = "{0}\n\n{1}".FormatWith(levelingStrings.requiredPageInstructions1, levelingStrings.requiredPageInstructions2);
				printLevelWizard.AddPage(new FirstPageInstructions(levelingStrings.initialPrinterSetupStepText, requiredPageInstructions));
			}

			printLevelWizard.AddPage(new FirstPageInstructions(levelingStrings.OverviewText, levelingStrings.WelcomeText(probeCount, 5)));

			printLevelWizard.AddPage(new HomePrinterPage(levelingStrings.homingPageStepText, levelingStrings.homingPageInstructions));

			string positionLabel = "Position".Localize();
			string autoCalibrateLabel = "Auto Calibrate".Localize();
			string lowPrecisionLabel = "Low Precision".Localize();
			string medPrecisionLabel = "Medium Precision".Localize();
			string highPrecisionLabel = "High Precision".Localize();

			double bedRadius = Math.Min(ActiveSliceSettings.Instance.GetValue<Vector2>(SettingsKey.bed_size).x, ActiveSliceSettings.Instance.GetValue<Vector2>(SettingsKey.bed_size).y) / 2;
			bool allowLessThanZero = ActiveSliceSettings.Instance.GetValue<bool>(SettingsKey.z_can_be_negative);

			double startProbeHeight = ActiveSliceSettings.Instance.GetValue<double>(SettingsKey.print_leveling_probe_start);
			for (int i = 0; i < probeCount; i++)
			{
				Vector2 probePosition = GetPrintLevelPositionToSample(i);

				if (ActiveSliceSettings.Instance.Helpers.UseZProbe())
				{
					var stepString = string.Format("{0} {1} {2} {3}:", levelingStrings.stepTextBeg, i + 1, levelingStrings.stepTextEnd, probeCount);
					printLevelWizard.AddPage(new AutoProbeFeedback(printLevelWizard, new Vector3(probePosition, startProbeHeight), string.Format("{0} {1} {2} - {3}", stepString, positionLabel, i + 1, autoCalibrateLabel), probePositions, i, allowLessThanZero));
				}
				else
				{
					printLevelWizard.AddPage(new GetCoarseBedHeight(printLevelWizard, new Vector3(probePosition, startProbeHeight), string.Format("{0} {1} {2} - {3}", levelingStrings.GetStepString(totalSteps), positionLabel, i + 1, lowPrecisionLabel), probePositions, i, allowLessThanZero));
					printLevelWizard.AddPage(new GetFineBedHeight(printLevelWizard, string.Format("{0} {1} {2} - {3}", levelingStrings.GetStepString(totalSteps), positionLabel, i + 1, medPrecisionLabel), probePositions, i, allowLessThanZero));
					printLevelWizard.AddPage(new GetUltraFineBedHeight(printLevelWizard, string.Format("{0} {1} {2} - {3}", levelingStrings.GetStepString(totalSteps), positionLabel, i + 1, highPrecisionLabel), probePositions, i, allowLessThanZero));
				}
			}

			printLevelWizard.AddPage(new LastPagelInstructions(printLevelWizard, "Done".Localize(), levelingStrings.DoneInstructions, probePositions));
		}

		public static MeshLevlingFunctions GetLevelingFunctions(int gridWidth, int gridHeight, PrintLevelingData levelingData)
		{
			if (currentLevelingFunctions == null
				|| !levelingData.SamplesAreSame(currentLevelingFunctions.SampledPositions))
			{
				if (currentLevelingFunctions != null)
				{
					currentLevelingFunctions.Dispose();
				}

				currentLevelingFunctions = new MeshLevlingFunctions(gridWidth, gridHeight, levelingData);
			}

			return currentLevelingFunctions;
		}

		new public abstract Vector2 GetPrintLevelPositionToSample(int index);
	}

	public class MeshLevlingFunctions : IDisposable
	{
		private Vector3 lastDestinationWithLevelingApplied = new Vector3();

		private EventHandler unregisterEvents;

		public MeshLevlingFunctions(int gridWidth, int gridHeight, PrintLevelingData levelingData)
		{
			this.SampledPositions = new List<Vector3>(levelingData.SampledPositions);

			PrinterConnectionAndCommunication.Instance.PositionRead.RegisterEvent(PrinterReportedPosition, ref unregisterEvents);

			for (int y = 0; y < gridHeight - 1; y++)
			{
				for (int x = 0; x < gridWidth - 1; x++)
				{
					// add all the regions
					Regions.Add(new Region()
					{
						LeftBottom = levelingData.SampledPositions[y * gridWidth + x],
						RightBottom = levelingData.SampledPositions[y * gridWidth + x + 1],
						LeftTop = levelingData.SampledPositions[(y + 1) * gridWidth + x],
						RightTop = levelingData.SampledPositions[(y + 1) * gridWidth + x + 1],
					});
				}
			}
		}

		// you can only set this on construction
		public List<Vector3> SampledPositions { get; private set; }

		public List<Region> Regions { get; private set; } = new List<Region>();

		public void Dispose()
		{
			unregisterEvents?.Invoke(this, null);
		}

		public string DoApplyLeveling(string lineBeingSent, Vector3 currentDestination,
			PrinterMachineInstruction.MovementTypes movementMode)
		{
			double extruderDelta = 0;
			GCodeFile.GetFirstNumberAfter("E", lineBeingSent, ref extruderDelta);
			double feedRate = 0;
			GCodeFile.GetFirstNumberAfter("F", lineBeingSent, ref feedRate);

			StringBuilder newLine = new StringBuilder("G1 ");

			if (lineBeingSent.Contains("X") || lineBeingSent.Contains("Y") || lineBeingSent.Contains("Z"))
			{
				Vector3 outPosition = GetPositionWithZOffset(currentDestination);

				if (movementMode == PrinterMachineInstruction.MovementTypes.Relative)
				{
					Vector3 delta = outPosition - lastDestinationWithLevelingApplied;
					lastDestinationWithLevelingApplied = outPosition;
					outPosition = delta;
				}
				else
				{
					lastDestinationWithLevelingApplied = outPosition;
				}

				newLine = newLine.Append(String.Format("X{0:0.##} Y{1:0.##} Z{2:0.###}", outPosition.x, outPosition.y, outPosition.z));
			}

			if (extruderDelta != 0)
			{
				newLine = newLine.Append(String.Format(" E{0:0.###}", extruderDelta));
			}

			if (feedRate != 0)
			{
				newLine = newLine.Append(String.Format(" F{0:0.##}", feedRate));
			}

			lineBeingSent = newLine.ToString();

			return lineBeingSent;
		}

		public Vector3 GetPositionWithZOffset(Vector3 currentDestination)
		{
			Region region = GetCorrectRegion(currentDestination);

			return region.GetPositionWithZOffset(currentDestination);
		}

		public Vector2 GetPrintLevelPositionToSample(int index, int gridWidth, int gridHeight)
		{
			var manualPositions = LevelWizardBase.GetManualPositions(ActiveSliceSettings.Instance.GetValue(SettingsKey.leveling_manual_positions), gridWidth * gridHeight);
			if (manualPositions != null)
			{
				return manualPositions[index];
			}

			Vector2 bedSize = ActiveSliceSettings.Instance.GetValue<Vector2>(SettingsKey.bed_size);
			Vector2 printCenter = ActiveSliceSettings.Instance.GetValue<Vector2>(SettingsKey.print_center);

			switch (ActiveSliceSettings.Instance.GetValue<BedShape>(SettingsKey.bed_shape))
			{
				case BedShape.Circular:
					Vector2 firstPosition = new Vector2(printCenter.x, printCenter.y + (bedSize.y / 2) * .5);
					switch (index)
					{
						case 0:
							return firstPosition;

						case 1:
							return Vector2.Rotate(firstPosition, MathHelper.Tau / 3);

						case 2:
							return Vector2.Rotate(firstPosition, MathHelper.Tau * 2 / 3);

						default:
							throw new IndexOutOfRangeException();
					}

				case BedShape.Rectangular:
				default:
					switch (index)
					{
						case 0:
							return new Vector2(printCenter.x, printCenter.y + (bedSize.y / 2) * .8);

						case 1:
							return new Vector2(printCenter.x - (bedSize.x / 2) * .8, printCenter.y - (bedSize.y / 2) * .8);

						case 2:
							return new Vector2(printCenter.x + (bedSize.x / 2) * .8, printCenter.y - (bedSize.y / 2) * .8);

						default:
							throw new IndexOutOfRangeException();
					}
			}
		}

		private Region GetCorrectRegion(Vector3 currentDestination)
		{
			int bestIndex = 0;
			double bestDist = double.PositiveInfinity;

			currentDestination.z = 0;
			for (int regionIndex = 0; regionIndex < Regions.Count; regionIndex++)
			{
				var dist = (Regions[regionIndex].Center - currentDestination).LengthSquared;
				if(dist < bestDist)
				{
					bestIndex = regionIndex;
					bestDist = dist;
				}
			}

			return Regions[bestIndex];
		}

		private void PrinterReportedPosition(object sender, EventArgs e)
		{
			lastDestinationWithLevelingApplied = GetPositionWithZOffset(PrinterConnectionAndCommunication.Instance.LastReportedPosition);
		}

		public class Region
		{
			public Vector3 LeftBottom { get; set; }
			public Vector3 LeftTop { get; set; }
			public Vector3 RightBottom { get; set; }
			public Vector3 RightTop { get; set; }

			internal Vector3 Center { get; private set; }
			internal Vector3 LeftBottomCenter { get; private set; }
			internal Vector3 RightTopCenter { get; private set; }

			internal Plane LeftBottomPlane { get; private set; }
			internal Plane RightTopPlane { get; private set; }

			internal Vector3 GetPositionWithZOffset(Vector3 currentDestination)
			{
				if (LeftBottomPlane.PlaneNormal == Vector3.Zero)
				{
					InitializePlanes();
				}

				var destinationAtZ0 = new Vector3(currentDestination.x, currentDestination.y, 0);

				// which triangle to check (distance to the centers)
				if ((LeftBottomCenter - destinationAtZ0).LengthSquared < (RightTopCenter - destinationAtZ0).LengthSquared)
				{
					double hitDistance = LeftBottomPlane.GetDistanceToIntersection(destinationAtZ0, Vector3.UnitZ);
					currentDestination.z += hitDistance;
				}
				else
				{
					double hitDistance = RightTopPlane.GetDistanceToIntersection(destinationAtZ0, Vector3.UnitZ);
					currentDestination.z += hitDistance;
				}

				return currentDestination;
			}

			private void InitializePlanes()
			{
				LeftBottomPlane = new Plane(LeftBottom, RightBottom, LeftTop);
				LeftBottomCenter = (LeftBottom + RightBottom + LeftTop) / 3;

				RightTopPlane = new Plane(RightBottom, RightTop, LeftTop);
				RightTopCenter = (RightBottom + RightTop + LeftTop) / 3;

				Center = (LeftBottomCenter + RightTopCenter) / 2;
			}
		}
	}
}