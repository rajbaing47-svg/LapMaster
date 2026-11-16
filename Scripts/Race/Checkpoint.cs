using UnityEngine;

// One of these sits on every checkpoint trigger in the scene, in race order.
// checkpointIndex 0 is the start/finish line; every other index (1, 2, 3...) is
// a checkpoint that must be collected, in order, before the finish line will
// count as completing a lap. See RaceProgress.HitCheckpoint for the actual
// collection/validation logic - this component just forwards the trigger event.
public class Checkpoint : MonoBehaviour
{
    public int checkpointIndex;

    private void OnTriggerEnter(Collider other)
    {
        RaceProgress rp = other.GetComponentInParent<RaceProgress>();
        if (rp != null) rp.HitCheckpoint(checkpointIndex);
    }
}