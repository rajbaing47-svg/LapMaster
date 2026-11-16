using UnityEngine;

// WHY THIS EXISTS: your player car is spawned at runtime by RaceManager
// (Instantiate(playerPrefab, ...); playerCar.tag = "Player";) - it does not exist in the
// Hierarchy when you're wiring up buttons in the Editor. So Event Trigger fields can't hold a
// direct reference to it. This script sits on a PERSISTENT object (put it on your mobile
// Canvas), and at runtime looks up whichever car currently has the "Player" tag, caches it,
// and forwards every button press/release to that car's InputManager.
//
// SETUP: wire your 6 buttons' Event Trigger (Pointer Down / Pointer Up) to THIS script's
// methods below instead of to the car directly - same method names as before.
public class MobileInputBridge : MonoBehaviour
{
    private InputManager cachedInput;

    private InputManager GetInput()
    {
        // Re-find if we don't have one yet, or if the previous one was destroyed
        // (e.g. going back to a menu and starting a new race spawns a new car).
        if (cachedInput == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) cachedInput = player.GetComponent<InputManager>();
        }
        return cachedInput;
    }

    public void MobileAccelerateDown() { GetInput()?.MobileAccelerateDown(); }
    public void MobileAccelerateUp() { GetInput()?.MobileAccelerateUp(); }

    public void MobileBrakeReverseDown() { GetInput()?.MobileBrakeReverseDown(); }
    public void MobileBrakeReverseUp() { GetInput()?.MobileBrakeReverseUp(); }

    public void MobileLeftDown() { GetInput()?.MobileLeftDown(); }
    public void MobileLeftUp() { GetInput()?.MobileLeftUp(); }

    public void MobileRightDown() { GetInput()?.MobileRightDown(); }
    public void MobileRightUp() { GetInput()?.MobileRightUp(); }

    public void MobileHandbrakeDown() { GetInput()?.MobileHandbrakeDown(); }
    public void MobileHandbrakeUp() { GetInput()?.MobileHandbrakeUp(); }

    public void MobileBoostDown() { GetInput()?.MobileBoostDown(); }
    public void MobileBoostUp() { GetInput()?.MobileBoostUp(); }
}