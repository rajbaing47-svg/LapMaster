using UnityEngine;

public class MinimapFollowOrtho : MonoBehaviour
{
    public Transform player;
    public float height = 100f;
    public float orthoSize = 50f;

    [Header("Rotation")]
    public bool rotateWithPlayer = true;
    public float rotationSmoothSpeed = 5f; // 0 = instant snap, higher = smoother

    private Camera cam;

    private void Start()
    {
        cam = GetComponent<Camera>();
        cam.orthographic = true;

        FindPlayer();
    }

    private void LateUpdate()
    {
        if (player == null)
        {
            FindPlayer();
            if (player == null) return;
        }

        transform.position = new Vector3(player.position.x, height, player.position.z);
        cam.orthographicSize = orthoSize;

        if (rotateWithPlayer)
        {
            // Camera looks straight down (X=90). To rotate the view with the player,
            // we spin it around its own forward (down) axis using the player's Y rotation.
            Quaternion targetRot = Quaternion.Euler(90f, player.eulerAngles.y, 0f);

            if (rotationSmoothSpeed <= 0f)
            {
                transform.rotation = targetRot;
            }
            else
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSmoothSpeed * Time.deltaTime);
            }
        }
        else
        {
            transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        }
    }

    private void FindPlayer()
    {
        GameObject found = GameObject.FindGameObjectWithTag("MiniCar");
        if (found != null)
        {
            player = found.transform;
        }
    }
}