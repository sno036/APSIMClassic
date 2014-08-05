using System;
using System.Collections.Generic;
using System.Text;
using Models.Core;

namespace Models.PMF.Organs
{
    [Serializable]
    public class GenericAboveGroundOrgan : GenericOrgan, AboveGround
    {
        [Link]
        ISummary Summary = null;

        #region Event handlers
        [EventSubscribe("Prune")]
        private void OnPrune(PruneType Prune)
        {
            Summary.WriteMessage(FullPath, "Pruning");

            Live.Clear();
            Dead.Clear();
        }
        public override void OnCut()
        {
            Summary.WriteMessage(FullPath, "Cutting");

            Live.Clear();
            Dead.Clear();
        }
        #endregion
    }
}
