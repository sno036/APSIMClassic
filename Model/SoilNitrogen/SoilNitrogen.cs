﻿using System;
using System.Reflection;
using System.Collections.Generic;
using System.Text;
using ModelFramework;
using CSGeneral;
using System.Xml;

    /// <summary>
    /// Initially ported from Fortran SoilN model by Eric Zurcher Sept/Oct-2010.
    /// Code tidied up by RCichota on Aug/Sep-2012: mostly modifying how some variables are handled (substitute 'get's by [input]), added regions to ease access,
    ///   updated error messages, moved all soilTemp code to a separate class (the idea is to eliminate it in the future), also added some of the constants to xml.
    /// Changes on Sep/Oct-2012 by RCichota, add patch capability: move all code for soil C and N to a separate class (SoilCNPatch), allow several instances to be
    ///   initialised, modified inputs to handle the partitioning of incoming N, also modified outputs to sum up the pools from the several instances (patches)
    /// </summary>
    public partial class SoilNitrogen
    {

        [Link]
        Component My = null;

        List<soilCNPatch> Patch;

        public SoilNitrogen()
        {
            Patch = new List<soilCNPatch>();
            soilCNPatch newPatch = new soilCNPatch();
            Patch.Add(newPatch);
            Patch[0].PatchArea = 1.0;
            Patch[0].PatchName = "base";
        }

        #region Events which we publish

        /// <summary>
        /// Event for communicating C and/or N changes to/from outside the simulation
        /// </summary>
        [Event]
        public event ExternalMassFlowDelegate ExternalMassFlow;

        [Event]
        public event NewSoluteDelegate new_solute;

        [Event]
        public event SurfaceOrganicMatterDecompDelegate actualresiduedecompositioncalculated;

        #endregion

        #region Setup events handlers and methods

        [EventHandler]
        public void OnInitialised()
        {
            // +  Purpose:
            //      Performs the initial checks and setup

            // set the size of arrays
            ResizeLayerArrays(dlayer.Length);
            foreach (soilCNPatch aPatch in Patch)
                aPatch.ResizeLayerArrays(dlayer.Length);

            // check few initialisation parameters
            CheckParams();

            // initialise first patch
            InitialisePatch(0);

            // perform initial calculations and setup
            InitCalc();

            // initialise soil temperature
            //if (use_external_st)
            //    st = ave_soil_temp;
            //else
            //{
            //    simpleST = new simpleSoilTemp(latitude, tav, amp, mint, maxt);
            //    st = simpleST.SoilTemperature(today, mint, maxt, radn, salb, dlayer, bd, ll15_dep, sw_dep);
            //}

            if(!use_external_st)
                simpleST = new simpleSoilTemp(latitude, tav, amp, mint, maxt);

            // notifify apsim about solutes
            AdvertiseMySolutes();

            // print SoilN report
            WriteSummaryReport();
        }

        [EventHandler(EventName = "reset")]
        public void OnReset()
        {
            // +  Purpose:
            //      Reset those values that the user may have changed since initialisation

            inReset = true;

            // Save present state
            SaveState();

            // reset the size of arrays - so it zeroes them
            ResizeLayerArrays(dlayer.Length);

            // reset patches
            Patch.Clear();
            soilCNPatch newPatch = new soilCNPatch();
            Patch.Add(newPatch);

            foreach (soilCNPatch aPatch in Patch)
                aPatch.ResizeLayerArrays(dlayer.Length);

            InitialisePatch(0);

            // reset C and N variables to their initial state
            oc = OC_reset;
            no3ppm = no3ppm_reset;
            nh4ppm = nh4ppm_reset;
            ureappm = ureappm_reset;

            // perform initial calculations and setup
            InitCalc();

            // reset soil temperature
            if (use_external_st)
                st = ave_soil_temp;
            else
            {
                simpleST = new simpleSoilTemp(latitude, tav, amp, mint, maxt);
                st = simpleST.SoilTemperature(today, mint, maxt, radn, salb, dlayer, bd, ll15_dep, sw_dep);
            }

            // get the changes of state and publish (let other component to know)
            DeltaState();

            // print SoilN report
            WriteSummaryReport();

            inReset = false;
        }

        private void CheckParams()
        {
            // +  Purpose:
            //      Check initialisation parameters and let user know

            Console.WriteLine();
            Console.WriteLine("        - Reading/checking parameters");

            SoilParamSet = SoilParamSet.Trim();

            Console.WriteLine("           - Using " + SoilParamSet + " soil mineralisation specification");

            // check whether soil temperature is present. If not, check whether the basic params for simpleSoilTemp have been supplied
            if (AllowsimpleSoilTemp)
                use_external_st = (ave_soil_temp != null);
            if (!use_external_st)
            {
                if (latitude == -999.0)
                    throw new Exception("Value for latitude was not supplied");
                if (tav == -999.0)
                    throw new Exception("Value for TAV was not supplied");
                if (amp == -999.0)
                    throw new Exception("Value for AMP was not supplied");
            }

            // check whether ph is supplied, use a default if not  - might be better to throw an exception?
            use_external_ph = (ph != null);
            if (!use_external_ph)
            {
                for (int layer = 0; layer < dlayer.Length; ++layer)
                    ph[layer] = 6.0; // ph_ini
            }

            // convert minimum values for nh4 and no3 from ppm to kg/ha
            for (int layer = 0; layer < dlayer.Length; ++layer)
            {
                double convFact = convFactor_kgha2ppm(layer);
                urea_min[layer] = MathUtility.Divide(ureappm_min, convFact, 0.0);
                nh4_min[layer] = MathUtility.Divide(nh4ppm_min, convFact, 0.0);
                no3_min[layer] = MathUtility.Divide(no3ppm_min, convFact, 0.0);
            }

            // Check if all fom values have been supplied
            if (num_fom_types != fract_carb.Length)
                throw new Exception("Number of \"fract_carb\" different to \"fom_type\"");
            if (num_fom_types != fract_cell.Length)
                throw new Exception("Number of \"fract_cell\" different to \"fom_type\"");
            if (num_fom_types != fract_lign.Length)
                throw new Exception("Number of \"fract_lign\" different to \"fom_type\"");

            // Check if all C:N values have been supplied. If not use average C:N ratio in all pools
            if (fomPools_cn == null || fomPools_cn.Length < 3)
            {
                fomPools_cn = new double[3];
                for (int i = 0; i < 3; i++)
                    fomPools_cn[i] = fom_ini_cn;
            }

            // Check if inital fom depth has been supplied, if not assume that initial fom is distributed over the whole profile
            if (fom_ini_depth == 0.0)
            {
                for (int i = 0; i < dlayer.Length; ++i)
                    fom_ini_depth += dlayer[i];
            }
        }

        private void InitCalc()
        {
            // +  Purpose:
            //      Do the initial setup and calculations - also used onReset

            int nLayers = dlayer.Length;

            // Factor to distribute fom over the soil profile. Uses a exponential function and goes till the especified depth
            double[] fom_FracLayer = new double[nLayers];
            double cum_depth = 0.0;
            int deepest_layer = getCumulativeIndex(fom_ini_depth, dlayer);
            for (int layer = 0; layer <= deepest_layer; layer++)
            {
                fom_FracLayer[layer] = Math.Exp(-3.0 * Math.Min(1.0, MathUtility.Divide(cum_depth + dlayer[layer], fom_ini_depth, 0.0))) *
                    Math.Min(1.0, MathUtility.Divide(fom_ini_depth - cum_depth, dlayer[layer], 0.0));
                cum_depth += dlayer[layer];
            }
            double fom_FracLayer_tot = SumDoubleArray(fom_FracLayer);

            // ensure initial OC has a value for each layer
            Array.Resize(ref OC_reset, nLayers);

            // Distribute an convert C an N values over the profile
            for (int layer = 0; layer < nLayers; layer++)
            {
                double convFact = convFactor_kgha2ppm(layer);
                double newValue = 0.0;
                // check and distribute the mineral nitrogen
                if (ureappm_reset != null)
                {
                    newValue = MathUtility.Divide(ureappm_reset[layer], convFact, 0.0);       //Convert from ppm to convFactor_kgha2ppm/ha
                    for (int k = 0; k < Patch.Count; k++)
                        Patch[k].urea[layer] = newValue;
                }
                newValue = MathUtility.Divide(nh4ppm_reset[layer], convFact, 0.0);       //Convert from ppm to convFactor_kgha2ppm/ha
                for (int k = 0; k < Patch.Count; k++)
                    Patch[k].nh4[layer] = newValue;
                newValue = MathUtility.Divide(no3ppm_reset[layer], convFact, 0.0);       //Convert from ppm to convFactor_kgha2ppm/ha
                for (int k = 0; k < Patch.Count; k++)
                    Patch[k].no3[layer] = newValue;

                // calculate total soil C
                double Soil_OC = OC_reset[layer] * 10000;     // = (oc/100)*1000000 - convert from % to ppm
                Soil_OC = MathUtility.Divide(Soil_OC, convFact, 0.0);  // kg/ha

                // calculate inert soil C
                double InertC = finert[layer] * Soil_OC;

                // calculate microbial biomass C and N
                double BiomassC = MathUtility.Divide((Soil_OC - InertC) * fbiom[layer], 1.0 + fbiom[layer], 0.0);
                double BiomassN = MathUtility.Divide(BiomassC, biom_cn, 0.0);

                // calculate C and N values for active humus
                double HumusC = Soil_OC - BiomassC;
                double HumusN = MathUtility.Divide(HumusC, hum_cn, 0.0);

                // distribute and calculate the fom N and C
                double fom = MathUtility.Divide(fom_ini_wt * fom_FracLayer[layer], fom_FracLayer_tot, 0.0);

                for (int k = 0; k < Patch.Count; k++)
                {
                    Patch[k].inert_c[layer] = InertC;
                    Patch[k].biom_c[layer] = BiomassC;
                    Patch[k].biom_n[layer] = BiomassN;
                    Patch[k].hum_c[layer] = HumusC;
                    Patch[k].hum_n[layer] = HumusN;
                    Patch[k].fom_c_pool1[layer] = fom * fract_carb[0] * c_in_fom;
                    Patch[k].fom_c_pool2[layer] = fom * fract_cell[0] * c_in_fom;
                    Patch[k].fom_c_pool3[layer] = fom * fract_lign[0] * c_in_fom;
                    Patch[k].fom_n_pool1[layer] = MathUtility.Divide(Patch[k].fom_c_pool1[layer], fomPools_cn[0], 0.0);
                    Patch[k].fom_n_pool2[layer] = MathUtility.Divide(Patch[k].fom_c_pool2[layer], fomPools_cn[1], 0.0);
                    Patch[k].fom_n_pool3[layer] = MathUtility.Divide(Patch[k].fom_c_pool3[layer], fomPools_cn[2], 0.0);
                }

                // store today's values
                for (int k = 0; k < Patch.Count; k++)
                    Patch[k].InitCalc();
            }

            // Calculations for NEW sysbal component
            dailyInitialC = SumDoubleArray(carbon_tot);
            dailyInitialN = SumDoubleArray(nit_tot);

            initDone = true;
        }

        private void ResizeLayerArrays(int nLayers)
        {
            // +  Purpose:
            //      Set the size of all public arrays (with nLayers), this doesn't clear the existing values

            Array.Resize(ref st, nLayers);
            Array.Resize(ref urea_min, nLayers);
            Array.Resize(ref nh4_min, nLayers);
            Array.Resize(ref no3_min, nLayers);
        }

        private void AdvertiseMySolutes()
        {
            // + Purpose
            //    Notify any interested modules about this module's ownership of solute information.

            if (new_solute != null)
            {
                string[] solute_names;
                if (useOrganicSolutes)
                {
                    solute_names = new string[7] { "urea", "nh4", "no3", "org_c_pool1", "org_c_pool2", "org_c_pool3", "org_n" };
                }
                else
                { // don't publish the organic solutes
                    solute_names = new string[3] { "urea", "nh4", "no3" };
                }

                NewSoluteType SoluteData = new NewSoluteType();
                //data.sender_id = (int)ParentComponent().GetId();      I think it should have the sender's name as well
                SoluteData.solutes = solute_names;

                new_solute.Invoke(SoluteData);
            }
        }

        private void SaveState()
        {
            // +  Purpose:
            //      Calculates variations in C an N, needed for both NEW and OLD sysbal component

            dailyInitialN = SumDoubleArray(nit_tot);
            dailyInitialC = SumDoubleArray(carbon_tot);
        }

        private void DeltaState()
        {
            // +  Purpose:
            //      Calculates variations in C an N, and publishes Mass flows to apsim

            double dltN = SumDoubleArray(nit_tot) - dailyInitialN;
            double dltC = SumDoubleArray(carbon_tot) - dailyInitialC;

            SendExternalMassFlowN(dltN);
            SendExternalMassFlowC(dltC);
        }

        private void WriteSummaryReport()
        {
            // +  Purpose:
            //      Write report about setup and status of SoilNitrogen

            string myMessage = "";
            if (use_external_st)
                myMessage = "   - Soil temperature supplied by apsim";
            else
                myMessage = "   - Soil temperature calculated internally";
            Console.WriteLine("        " + myMessage);
            if (use_external_ph)
                myMessage = "   - Soil pH supplied by apsim";
            else
                myMessage = "   - Soil pH was not supplied, default value will be used";
            Console.WriteLine("        " + myMessage);
            Console.WriteLine();

            Console.Write(@"
                      Soil Profile Properties
          ------------------------------------------------
           Layer    pH    OC     NO3     NH4    Urea
                         (%) (kg/ha) (kg/ha) (kg/ha)
          ------------------------------------------------
");
            for (int layer = 0; layer < dlayer.Length; ++layer)
            {
                Console.WriteLine("          {0,4:d1}     {1,4:F2}  {2,4:F2}  {3,6:F2}  {4,6:F2}  {5,6:F2}",
                layer + 1, ph[layer], OC_reset[layer], no3[layer], nh4[layer], urea[layer]);
            }
            Console.WriteLine("          ------------------------------------------------");
            Console.WriteLine("           Totals              {0,6:F2}  {1,6:F2}  {2,6:F2}",
                      SumDoubleArray(no3), SumDoubleArray(nh4), SumDoubleArray(urea));
            Console.WriteLine("          ------------------------------------------------");
            Console.WriteLine();
            Console.Write(@"
                  Initial Soil Organic Matter Status
          ---------------------------------------------------------
           Layer      Hum-C   Hum-N  Biom-C  Biom-N   FOM-C   FOM-N
                    (kg/ha) (kg/ha) (kg/ha) (kg/ha) (kg/ha) (kg/ha)
          ---------------------------------------------------------
");

            double TotalFomC = 0.0;
            for (int layer = 0; layer < dlayer.Length; ++layer)
            {
                //double FomC = fom_c_pool1[layer] + fom_c_pool2[layer] + fom_c_pool3[layer];
                TotalFomC += fom_c[layer];
                Console.WriteLine("          {0,4:d1}   {1,10:F1}{2,8:F1}{3,8:F1}{4,8:F1}{5,8:F1}{6,8:F1}",
                layer + 1, hum_c[layer], hum_n[layer], biom_c[layer], biom_n[layer], fom_c[layer], fom_n[layer]);
            }
            Console.WriteLine("          ---------------------------------------------------------");
            Console.WriteLine("           Totals{0,10:F1}{1,8:F1}{2,8:F1}{3,8:F1}{4,8:F1}{5,8:F1}",
                SumDoubleArray(hum_c), SumDoubleArray(hum_n), SumDoubleArray(biom_c),
                SumDoubleArray(biom_n), TotalFomC, SumDoubleArray(fom_n));
            Console.WriteLine("          ---------------------------------------------------------");
            Console.WriteLine();
        }

        #endregion

        #region Process events handlers and methods

        #region Daily processes

        [EventHandler(EventName = "process")]
        public void OnProcess()
        {
            // + Purpose:
            //     Performs every-day calculations - main process phase

            // update soil temperature
            if (use_external_st)
                st = ave_soil_temp;
            else
                st = simpleST.SoilTemperature(today, mint, maxt, radn, salb, dlayer, bd, ll15_dep, sw_dep);
            
            // update patch data
            UpdatePatches();

            // calculate C and N processes
            Process();

            // send actual decomposition back to surface OM
            if (!is_pond_active)
                SendActualResidueDecompositionCalculated();
        }

        [EventHandler(EventName = "tick")]
        public void OnTick(TimeType time)
        {
            // + Purpose:
            //     Reset potential decomposition variables and get initial C and N status

            foreach (soilCNPatch aPatch in Patch)
                aPatch.OnTick();

            // Calculations for NEW sysbal component
            dailyInitialC = SumDoubleArray(carbon_tot);
            dailyInitialN = SumDoubleArray(nit_tot);
        }

        [EventHandler(EventName = "post")]
        public void OnPost()
        {
            // + Purpose:
            //     Performs every-day calculations - end of day processes

            if (Patch.Count > 10) // must set this to one later
            {
                // we have more than one patch, check whether they are similar enough to be merged
                PatchIDs Patches = new PatchIDs();
                Patches = ComparePatches();

                if (Patches.disappearing.Count > 0)
                {  // there are patches that will be merged
                    for (int k = 0; k < Patches.disappearing.Count; k++)
                    {
                        MergePatches(Patches.recipient[k], Patches.disappearing[k]);
                        Console.WriteLine(today.ToString("dd MMMM yyyy") + "(Day of year=" + today.DayOfYear.ToString() + "), SoilNitrogen.MergePatch:");
                        Console.WriteLine("   merging Patch(" + Patches.disappearing[k].ToString() + ") into Patch(" + Patches.recipient[k].ToString() +
                            "). New patch area = " + Patch[Patches.recipient[k]].PatchArea.ToString("#0.00#"));
                    }
                }
            }
        }

        private void Process()
        {
            // + Purpose
            //     This routine performs the soil C and N balance, daily.
            //      - Assesses potential decomposition of surface residues (adjust decompostion if needed, accounts for mineralisation/immobilisation of N)
            //      - Calculates hydrolysis of urea, denitrification, transformations on soil organic matter (including N mineralisation/immobilition) and nitrification.

            for (int k = 0; k < Patch.Count; k++)
                Patch[k].Process();
        }

        private void SendActualResidueDecompositionCalculated()
        {
            // + Purpose
            //     Send back the information about actual decomposition
            //      Potential decomposition was given to this module by a residue/surfaceOM module.  Now we explicitly tell the module the actual decomposition
            //      rate for each of its residues.  If there wasn't enough mineral N to decompose, the rate will be reduced from the potential value.

            if (actualresiduedecompositioncalculated != null)
            {

                // will have to pack the SOMdecomp data from each patch and then invoke the event
                //int num_residues = Patch[0].SOMDecomp.Pool.Length;
                int nLayers = dlayer.Length;

                SurfaceOrganicMatterDecompType SOMDecomp = new SurfaceOrganicMatterDecompType();
                Array.Resize(ref SOMDecomp.Pool, num_residues);

                for (int residue = 0; residue < num_residues; residue++)
                {
                    float c_summed = 0.0F;
                    float n_summed = 0.0F;
                    for (int k = 0; k < Patch.Count; k++)
                    {
                        c_summed += Patch[k].SOMDecomp.Pool[residue].FOM.C * (float)Patch[k].PatchArea;
                        n_summed += Patch[k].SOMDecomp.Pool[residue].FOM.N * (float)Patch[k].PatchArea;
                    }

                    SOMDecomp.Pool[residue] = new SurfaceOrganicMatterDecompPoolType();
                    SOMDecomp.Pool[residue].FOM = new FOMType();
                    SOMDecomp.Pool[residue].Name = Patch[0].SOMDecomp.Pool[residue].Name;
                    SOMDecomp.Pool[residue].OrganicMatterType = Patch[0].SOMDecomp.Pool[residue].OrganicMatterType;
                    SOMDecomp.Pool[residue].FOM.amount = 0.0F;
                    SOMDecomp.Pool[residue].FOM.C = c_summed;
                    SOMDecomp.Pool[residue].FOM.N = n_summed;
                    SOMDecomp.Pool[residue].FOM.P = 0.0F;
                    SOMDecomp.Pool[residue].FOM.AshAlk = 0.0F;
                }

                // send the decomposition information
                actualresiduedecompositioncalculated.Invoke(SOMDecomp);
            }
        }

        #endregion

        #region Frequent and sporadic processes

        [EventHandler(EventName = "sum_report")]
        public void OnSum_report()
        {
            WriteSummaryReport();
        }

        [EventHandler(EventName = "IncorpFOM")]
        public void OnIncorpFOM(FOMLayerType inFOMdata)
        {
            // +  Purpose:
            //      Partition the given FOM C and N into fractions in each layer.
            //      In this event all FOM is given as one, so it will be assumed that the CN ratios of all fractions are equal

            foreach (soilCNPatch aPatch in Patch)
                aPatch.OnIncorpFOM(inFOMdata);

            fom_type = Patch[0].fom_type;
        }

        [EventHandler(EventName = "IncorpFOMPool")]
        public void OnIncorpFOMPool(FOMPoolType inFOMPoolData)
        {
            // +  Purpose:
            //      Partition the given FOM C and N into fractions in each layer.
            //      In this event each of the three pools is given

            foreach (soilCNPatch aPatch in Patch)
                aPatch.OnIncorpFOMPool(inFOMPoolData);
        }

        [EventHandler(EventName = "PotentialResidueDecompositionCalculated")]
        public void OnPotentialResidueDecompositionCalculated(SurfaceOrganicMatterDecompType SurfaceOrganicMatterDecomp)
        {
            //+  Purpose
            //     Get information of potential residue decomposition

            foreach (soilCNPatch aPatch in Patch)
                aPatch.OnPotentialResidueDecompositionCalculated(SurfaceOrganicMatterDecomp);

            num_residues = SurfaceOrganicMatterDecomp.Pool.Length;
        }

        [EventHandler(EventName = "new_profile")]
        public void OnNew_profile(NewProfileType NewProfile)
        {
            //+  Purpose
            //     Consider soil profile changes - primarily due to by erosion (??)

            foreach (soilCNPatch aPatch in Patch)
                aPatch.OnNew_profile(NewProfile);
        }

        [EventHandler(EventName = "NitrogenChanged")]
        public void OnNitrogenChanged(NitrogenChangedType NitrogenChanges)
        {
            //+  Purpose
            //     Get the delta mineral N from other module
            //     Send deltas to each patch, if delta comes from soil or plant then the values are modified (partioned)
            //      based on N content. If sender is any other module then values are passed to patches as they come

            string module = NitrogenChanges.Sender.ToLower();
            if (module == "soilwat" || module == "agpasture" || module == "plant")
            {
                // values supplied by a module from which a different treatment for each patch is required,
                //  they will be partitioned according to the N content in each patch

                // 1- consider urea:
                if (hasValues(NitrogenChanges.DeltaUrea, epsilon))
                {
                    // send incoming dlt to be partitioned amongst patches
                    double[][] newDelta = partitionDelta(NitrogenChanges.DeltaUrea, "urea");
                    // 1.1- send dlt's to each patch
                    for (int k = 0; k < Patch.Count; k++)
                        Patch[k].dlt_urea = newDelta[k];
                }

                // 2- consider nh4:
                if (hasValues(NitrogenChanges.DeltaNH4, epsilon))
                {
                    // send incoming dlt to be partitioned amongst patches
                    double[][] newDelta = partitionDelta(NitrogenChanges.DeltaNH4, "nh4");
                    // 2.1- send dlt's to each patch
                    for (int k = 0; k < Patch.Count; k++)
                        Patch[k].dlt_nh4 = newDelta[k];
                }

                // 3- consider no3:
                if (hasValues(NitrogenChanges.DeltaNO3, epsilon))
                {
                    // send incoming dlt to be partitioned amongst patches
                    double[][] newDelta = partitionDelta(NitrogenChanges.DeltaNO3, "no3");
                    // 3.1- send dlt's to each patch
                    for (int k = 0; k < Patch.Count; k++)
                        Patch[k].dlt_no3 = newDelta[k];
                }
            }
            else
            {
                // values will passed to patches as they come
                for (int k = 0; k < Patch.Count; k++)
                {
                    Patch[k].dlt_urea = NitrogenChanges.DeltaUrea;
                    Patch[k].dlt_nh4 = NitrogenChanges.DeltaNH4;
                    Patch[k].dlt_no3 = NitrogenChanges.DeltaNO3;
                }
            }
        }

        [EventHandler(EventName = "AddUrine")]
        public void OnAddUrine(AddUrineType UrineAdded)
        {
            //+  Purpose
            //     Add urine

            // Starting with the minimalist version. To be updated by Val's group to include a urine patch algorithm

            // test for adding urine patches
            // if VolumePerUrination = 0.0 then no patch will be added, otherwise a patch will be added (based on 'base' patch)
            // assuming new PatchArea is passed as a fraction and this will be subtracted from original
            // urea will be added to the top layer for now

            double[] newUrea = new double[dlayer.Length];
            newUrea[0] = UrineAdded.Urea;

            if (UrineAdded.VolumePerUrination > 0.0)
            {
                SplitPatch(0);
                double oldArea = Patch[0].PatchArea;
                double newArea = oldArea - UrineAdded.AreaPerUrination;
                Patch[0].PatchArea = newArea;
                int k = Patch.Count - 1;  // make it explicit for now to ease reading
                Patch[k].PatchArea = UrineAdded.AreaPerUrination;
                Patch[k].PatchName = "Patch" + k.ToString();
                if (UrineAdded.Urea > epsilon)
                    Patch[k].dlt_urea = newUrea;
            }
            else
                for (int k = 0; k < Patch.Count; k++)
                    Patch[k].dlt_urea = newUrea;

        }

        //    public void OnAddSoilCNPatch(AddSoilCNPatchType PatchtoAdd)
        [EventHandler(EventName = "AddSoilCNPatch")]
        public void OnAddSoilCNPatch(AddSoilCNPatchType PatchtoAdd)
        {
            //+  Purpose
            //     Handling the addition of urine patches

            // data passed with this event:
            //.Sender: the name of the module that raised this event
            //.DepositionType: the type of deposition:
            //  - Homogeneous: No patch is created, add urine as given to all patches. It is the default
            //  - ToSpecificPatch: No patch is created, add urine to selected patches (given by id or name if supplied, or default to Homogenous)
            //  - NewSimplePatch: create new patch(es) based on an existing patch (given by id or name if supplied, or by default the base/Patch[0])
            //  - NewOverlappingPatch, create new patch(es), overlaps with all existing patches, provided the new area is larger than a minimum (minPatchArea)
            //.AddToPatches_id: the index of the existing patches to which urine will be added
            //.AddToPatches_nm: the name of the existing patches to which urine will be added
            //.PatchArea: the relative area of the patch (0-1)
            //.UrineWater: amount of water to add, not handled here
            //.Urea: amount of urea to add, per layer

            double[] deltaUrea = new double[PatchtoAdd.Urea.Length];
            double totalUrea = SumDoubleArray(deltaUrea);

            //int[] fakeIDs = new int[PatchtoAdd.AddToPatches_id.Length];
            List<int> PatchesToDelete = new List<int>();

            if ((PatchtoAdd.DepositionType.ToLower() == "NewSimplePatch".ToLower()) || (PatchtoAdd.DepositionType.ToLower() == "NewOverlappingPatch".ToLower()))
            {
                // get the list of patch id's to which urine will be added, and the area affected
                int[] PatchIDs;
                double AreaAffected = 0;
                if (PatchtoAdd.DepositionType.ToLower() == "NewOverlappingPatch".ToLower())
                {
                    PatchIDs = new int[Patch.Count - 1];
                    for (int k = 0; k < Patch.Count; k++)
                        PatchIDs[k] = k;
                    AreaAffected = 1.0;
                }
                else
                {
                    PatchIDs = CheckPatchIDs(PatchtoAdd.AddToPatches_id, PatchtoAdd.AddToPatches_nm);
                    for (int i = 0; i < PatchIDs.Length; i++)
                        AreaAffected += Patch[PatchIDs[i]].PatchArea;
                }

                for (int i = 0; i < PatchIDs.Length; i++)
                {
                    // check the areas:
                    double OldPatch_oldArea = Patch[PatchIDs[i]].PatchArea;
                    double NewPatch_area = (OldPatch_oldArea / AreaAffected) * PatchtoAdd.PatchArea;
                    double OldPatch_newArea = NewPatch_area - NewPatch_area;
                    if (NewPatch_area < minPatchArea)
                    {  // area to create is too small, patch will not be created
                        Console.WriteLine(today.ToString("dd MMMM yyyy") + "(Day of year=" + today.DayOfYear.ToString() + "), SoilNitrogen.AddPatch:");
                        Console.WriteLine("   attempt to create a new patch with area too small or negative (" + NewPatch_area.ToString("#0.00#") + "). The patch will not be created");
                    }
                    else if (OldPatch_newArea < minPatchArea)
                    {  // negative or too small area, patch will be created but old one will be deleted
                        Console.WriteLine(today.ToString("dd MMMM yyyy") + "(Day of year=" + today.DayOfYear.ToString() + "), SoilNitrogen.AddPatch:");
                        Console.WriteLine(" attempt to set the area of existing patch(" + PatchIDs[i].ToString() + ") to a value too small or negative (" + OldPatch_newArea.ToString("#0.00#") + "). The patch will be eliminated");

                        // mark old patch for deletion
                        PatchesToDelete.Add(PatchIDs[i]);

                        // create new patch by re-using the old one
                        soilCNPatch OldPatch = Patch[PatchIDs[i]];
                        Patch.Add(OldPatch);
                        int k = Patch.Count - 1;
                        Patch[k].PatchName = "Patch" + k.ToString();
                        Patch[k].dlt_urea = deltaUrea;
                        Console.WriteLine(" create new patch, with area = " + OldPatch_oldArea.ToString("#0.00#") + ", based on extint patch(" + PatchIDs[i].ToString() +
                            "), area = " + OldPatch_oldArea.ToString("#0.00#"));
                    }
                    else
                    {  // create new patch by spliting an existing one
                        SplitPatch(PatchIDs[i]);
                        Patch[PatchIDs[i]].PatchArea = OldPatch_newArea;
                        int k = Patch.Count - 1;
                        Patch[k].PatchArea = NewPatch_area;
                        Patch[k].PatchName = "Patch" + k.ToString();
                        Patch[k].dlt_urea = deltaUrea;
                        Console.WriteLine(today.ToString("dd MMMM yyyy") + "(Day of year=" + today.DayOfYear.ToString() + "), SoilNitrogen.AddPatch:");
                        Console.WriteLine(" create new patch, with area = " + NewPatch_area.ToString("#0.00#") + ", based on existing patch(" + PatchIDs[i].ToString() +
                            ") - Old area = " + NewPatch_area.ToString("#0.00#") + ", new area = " + NewPatch_area.ToString("#0.00#"));
                    }
                }
            }
            else if (PatchtoAdd.DepositionType.ToLower() == "ToSpecificPatch".ToLower())
            {
                // add urine to selected patches, no new patch will be created
                // 1. check whether N is being added, if not skip this
                if (totalUrea > epsilon)
                {
                    // 2. get the list of patch id's to which urine will be added
                    int[] PatchIDs = CheckPatchIDs(PatchtoAdd.AddToPatches_id, PatchtoAdd.AddToPatches_nm);
                    // 3. Add deltaUrea to each patch
                    for (int i = 0; i < PatchIDs.Length; i++)
                        Patch[i].dlt_urea = deltaUrea;
                }
            }
            else
                // add urine to all existing patches, no new patch will be created
                // 1. check whether N is being added, if not skip this
                if (totalUrea > epsilon)
                {
                    // 2. Add deltaUrea to each patch
                    for (int k = 0; k < Patch.Count; k++)
                        Patch[k].dlt_urea = deltaUrea;
                }
        }

        private void SendExternalMassFlowN(double dltN)
        {
            // + Purpose
            //     Let other components know that N amount in the soil has changed

            ExternalMassFlowType massBalanceChange = new ExternalMassFlowType();
            if (Math.Abs(dltN) <= epsilon)
                dltN = 0.0;
            massBalanceChange.FlowType = dltN >= 0 ? "gain" : "loss";
            massBalanceChange.PoolClass = "soil";
            massBalanceChange.N = (float)Math.Abs(dltN);
            ExternalMassFlow.Invoke(massBalanceChange);
        }

        private void SendExternalMassFlowC(double dltC)
        {
            // + Purpose
            //     Let other components know that soil C has changed

            ExternalMassFlowType massBalanceChange = new ExternalMassFlowType();
            if (Math.Abs(dltC) <= epsilon)
                dltC = 0.0;
            massBalanceChange.FlowType = dltC >= 0 ? "gain" : "loss";
            massBalanceChange.PoolClass = "soil";
            massBalanceChange.N = (float)Math.Abs(dltC);
            ExternalMassFlow.Invoke(massBalanceChange);
        }

        #endregion

        #endregion

        #region Auxiliary functions

        private double convFactor_kgha2ppm(int layer)
        {
            // + Purpose
            //     Calculate conversion factor from kg/ha to ppm (mg/kg)

            if (bd == null || dlayer == null || bd.Length == 0 || dlayer.Length == 0)
            {
                return 0.0;
                throw new Exception(" Error on computing convertion factor, kg/ha to ppm. Value for dlayer or bulk density not valid");
            }
            return MathUtility.Divide(100.0, bd[layer] * dlayer[layer], 0.0);
        }

        private bool hasValues(double[] anArray, double MinValue)
        {
            // + Purpose
            //     check whether there is any considerable values in the array

            bool result = false;
            if (anArray != null)
            {
                foreach (double Value in anArray)
                {
                    if (Math.Abs(Value) > MinValue)
                    {
                        result = true;
                        break;
                    }
                }
            }
            return result;
        }

        private double SumDoubleArray(double[] anArray)
        {
            // + Purpose
            //     calculate the sum of all values of an array of doubles

            double result = 0.0;
            if (anArray != null)
            {
                foreach (double Value in anArray)
                    result += Value;
            }
            return result;
        }

        private int getCumulativeIndex(double sum, float[] realArray)
        {
            // + Purpose
            //     get the index at which the cumulative amount is equal or greater than 'sum'

            float cum = 0.0f;
            for (int i = 0; i < realArray.Length; i++)
            {
                cum += realArray[i];
                if (cum >= sum)
                    return i;
            }
            return realArray.Length - 1;
        }

        #endregion
    }


    public class SoilTypeDefinition
    {
        [Param]
        protected XmlNode SoilTypeDefinitionXML;
    }
