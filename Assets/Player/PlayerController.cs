using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Linq;
using Unity.Netcode;
using Unity.Netcode.Samples;
using NaughtyAttributes;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(ClientNetworkTransform))]
public class PlayerController : NetworkBehaviour
{
    public enum PlayerState
    {
        Idle,
        Moving,
        Dashing,
        Ungrounded
    }

    public enum PlayerCarryState
    {
        Empty,
        CarryingObject,
        CarryingPlayer
    }

    [Header("Config")]
    public float moveSpeed;
    public float rotateSpeed;
    public float dashDuration;
    public float dashForce;
    public float dashCooldown;
    public float throwForce;

    [Header("Character")]
    public Character characterObject;

    [Header("State (ReadOnly)")]
    [SerializeField] [ReadOnly] public PlayerState playerState;
    [SerializeField] [ReadOnly] public PlayerCarryState carryState;
    [SerializeField] [ReadOnly] public Pollutant carriedObject;
    [SerializeField] [ReadOnly] public bool isAlive;

    [Header("Variables (ReadOnly)")]
    [SerializeField] [ReadOnly] public List<GameObject> reachableCollectables;
    [SerializeField] [ReadOnly] private Vector2 movement;
    [SerializeField] [ReadOnly] private Vector3 lookVector;
    [SerializeField] [ReadOnly] private float timeOfLastDash;
    [SerializeField] private Vector2 defaultPositionRange = new Vector2(-5, 5);

    private Transform holdLocation;
    private LineRenderer aimIndicator;
    private Rigidbody rb;
    private PlayerControlsMapping controls;
    private Transform debugCanvasObj;
    public bool canMove;

    private GameObject heldObject;
    private bool justThrew;

    private NetworkVariable<PlayerCarryState> networkCarryState = new NetworkVariable<PlayerCarryState>();

    private void Awake()
    {
        // setup variables
        canMove = GameController.Instance.gameState.Value == GameController.GameState.Running;
        aimIndicator = transform.Find("ThrowIndicator").GetComponent<LineRenderer>();
        debugCanvasObj = transform.GetComponentInChildren<PlayerDebugUI>().transform;
        isAlive = true;
        lookVector = transform.forward;
        timeOfLastDash = 0;
        carryState = PlayerCarryState.Empty;
        playerState = PlayerState.Idle;
        rb = GetComponent<Rigidbody>();
        holdLocation = transform.Find("HoldLocation");

        justThrew = false;

        controls = new PlayerControlsMapping();

        // map control inputs
        controls.Gameplay.Dash.performed += ctx => DashPerformed();
        controls.Gameplay.Move.performed += ctx => MovePerformed(ctx.ReadValue<Vector2>());
        controls.Gameplay.Move.canceled += ctx => MoveCancelled();
        controls.Gameplay.Grab.started += ctx => GrabStarted();
        controls.Gameplay.Grab.canceled += ctx => GrabCancelled();
        controls.Gameplay.Throw.canceled += ctx => ThrowPerformed();
        controls.Gameplay.Throw.started += ctx => ThrowStarted();
    }

    private void Start()
    {
        // refresh the character
        RefreshCharacter();

        // setup debugging
        debugCanvasObj.gameObject.SetActive(FindObjectOfType<GameController>().isDebugEnabled);

        // Random Spawn Position:
        transform.position = new Vector3(Random.Range(defaultPositionRange.x, defaultPositionRange.y), 0, Random.Range(defaultPositionRange.x, defaultPositionRange.y));

        // NETWORKING:
        UpdatePlayerCarryStateServerRpc(PlayerCarryState.Empty);
    }

    void Update()
    {
        if (IsClient && IsOwner)
        {
            if (isAlive && canMove)
            {
                PlayerMovement();
            }

            else
            {
                // do dead things
            }
        }

        ClientVisuals();
    }

    private void ClientVisuals()
    {
        if (networkCarryState.Value == PlayerCarryState.Empty)
        {
            // Set "Held Object" to inactive
            transform.GetChild(1).gameObject.SetActive(false);
        }

        else if (networkCarryState.Value == PlayerCarryState.CarryingObject)
        {
            // Set "Held Object" to active
            transform.GetChild(1).gameObject.SetActive(true);
        }

        else
        {
            // Carrying Player
        }
    }

    private void PlayerMovement()
    {
        // calculate useful variables once
        float currentTime = Time.time;

        // switch on playerstate
        switch (playerState)
        {
            case PlayerState.Idle:
                // clear rotatitonal velocity
                rb.angularVelocity = Vector3.zero;

                // play idle animation, etc.
                break;

            case PlayerState.Moving:
                // handle player movement
                Vector3 movementVec = new Vector3(movement.x, 0, movement.y) * Time.deltaTime * moveSpeed;
                //rb.AddForce(movementVec, ForceMode.Impulse);
                //transform.Translate(movementVec, Space.World);
                rb.MovePosition(rb.position + movementVec);

                // rotate towards motion vector
                lookVector = movementVec.normalized;
                lookVector.y = 0f; // remove any y angle from the look vector

                transform.LookAt(Vector3.Lerp(transform.position + transform.forward, transform.position + lookVector, rotateSpeed * Time.deltaTime));
                // transform.rotation.SetFromToRotation(transform.rotation.eulerAngles, movementVec);

                // DEBUG:
                // draw motion vector
                Debug.DrawRay(transform.position, movementVec.normalized * 2, Color.blue);

                // draw facing vector
                Debug.DrawRay(transform.position, transform.forward * 2, Color.green);
                break;

            case PlayerState.Dashing:
                // check if the dash should be complete
                if ((currentTime - timeOfLastDash) >= dashDuration)
                {
                    // complete the dash
                    playerState = (movement.magnitude == 0) ? PlayerState.Idle : PlayerState.Moving;
                }

                else
                {
                    Debug.DrawLine(rb.position, rb.position + (lookVector * 4), Color.red);

                    // continue performing the dash
                    //transform.Translate(lookVector * dashForce * Time.deltaTime, Space.World);
                    //rb.MovePosition(rb.position + lookVector * dashForce * Time.deltaTime);
                    rb.AddForce(lookVector * dashForce, ForceMode.Impulse);
                    //rb.velocity = lookVector * dashForce;

                    playerState = PlayerState.Dashing;
                }

                break;

            case PlayerState.Ungrounded:
                // play a flail animation, etc.
                break;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        // switch on the other object's tag
        switch (other.tag)
        {
            case "Pollutant":
                // add the pollutant to the list of reachable collectables
                if (!reachableCollectables.Contains(other.gameObject) && !justThrew)
                {
                    reachableCollectables.Add(other.gameObject);
                }

                Debug.Log("ENTER: " + other.gameObject.GetInstanceID());
                break;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        // switch on the other object's tag
        switch (other.tag)
        {
            case "Pollutant":
                // remove the pollutant from the list of reachable collectables
                reachableCollectables.Remove(other.gameObject);
                Debug.Log("EXIT: " + other.gameObject.GetInstanceID());
                break;
        }
    }

    [NaughtyAttributes.Button("Refresh Character", EButtonEnableMode.Editor)]
    private void RefreshCharacter()
    {
        // check if there is an existing mesh
        Transform oldCharacter = transform.Find("Character");
        if (oldCharacter != null)
        {
            // remove the old mesh
            DestroyImmediate(oldCharacter.gameObject);
        }

        // instantiate the new mesh
        GameObject newMesh = Instantiate(characterObject.characterPrefab, transform);
        newMesh.name = "Character";
    }

    [NaughtyAttributes.Button("Refresh Reachable Collectables")]
    private void RefreshReachableCollectables()
    {
        for (int i = 0; i < reachableCollectables.Count; i++)
        {
            // remove missing reachable collectables
            if (reachableCollectables[i] == null)
                reachableCollectables.RemoveAt(i);
        }

        Debug.Log(reachableCollectables);
    }

    private void MovePerformed(Vector2 newMovement)
    {
        if (Application.isFocused)
        {
            // update the movement vector
            movement = newMovement;

            // set the playerstate to moving if not dashing
            if (playerState != PlayerState.Dashing)
                playerState = PlayerState.Moving;
        }
    }

    private void MoveCancelled()
    {
        if (Application.isFocused)
        {
            // reset the movement vector
            movement = Vector2.zero;

            // set playerstate to idle if not dashing
            if (playerState != PlayerState.Dashing)
                playerState = PlayerState.Idle;
        }
    }

    private void DashPerformed()
    {
        if (Application.isFocused)
        {
            // calculate the time since the last dash, and if the player can dash
            float timeSinceDashCompleted = (Time.time - timeOfLastDash) - dashDuration;
            bool canDash = playerState != PlayerState.Dashing && timeSinceDashCompleted >= dashCooldown;

            // make sure the player is not already dashing
            if (canDash)
            {
                timeOfLastDash = Time.time;

                // set the playerstate to dashing
                playerState = PlayerState.Dashing;
            }
        }
    }

    private void GrabStarted()
    {
        if (IsClient && IsOwner)
        {
            // refresh reachable collectables
            RefreshReachableCollectables();

            // determine if can pickup
            bool canPickup = (carryState == PlayerCarryState.Empty) && (reachableCollectables.Count > 0);

            Debug.Log("CanPickup: " + canPickup);

            // if the player can pickup
            if (canPickup)
            {
                // sort reachableCollectables by distance
                reachableCollectables = reachableCollectables.OrderBy(
                    r => Vector3.Distance(transform.position, r.transform.position)).ToList();

                // get the nearest reachable collectable
                GameObject nearestReachableCollectable = reachableCollectables[0];

                // Store the nearest reachable collectable for dropping purposes:
                heldObject = nearestReachableCollectable;

                // Call OnPickup ServerRpc:
                nearestReachableCollectable.GetComponent<PollutantBehaviour>().OnPickupServerRpc();

                reachableCollectables.Remove(nearestReachableCollectable);

                // update the carryState
                carryState = PlayerCarryState.CarryingObject;
                UpdatePlayerCarryStateServerRpc(PlayerCarryState.CarryingObject);
            }
        }
    }

    private void GrabCancelled()
    {
        if (IsClient && IsOwner)
        {
            // determine if can drop
            bool canDrop = (carryState == PlayerCarryState.CarryingObject) || (carryState == PlayerCarryState.CarryingPlayer);

            // if the player can drop
            if (canDrop)
            {
                // hide the aim indicator (in case throw is being held)
                aimIndicator.gameObject.SetActive(false);

                StartCoroutine(TempDisablePickup());
                var playerVelocity = 2f * new Vector3(movement.x, 0, movement.y);
                heldObject.GetComponent<PollutantBehaviour>().OnDropServerRpc(transform.position, transform.forward, playerVelocity, lookVector, throwForce, false);

                // update the carryState
                carryState = PlayerCarryState.Empty;
                UpdatePlayerCarryStateServerRpc(PlayerCarryState.Empty);
            }
        }
    }

    private void ThrowStarted()
    {
        if (IsClient && IsOwner)
        {
            // determine if can throw
            bool canThrow =
            (playerState == PlayerState.Idle || playerState == PlayerState.Moving) &&
            (carryState == PlayerCarryState.CarryingObject || carryState == PlayerCarryState.CarryingPlayer);

            if (canThrow)
            {
                // show the aim indicator
                aimIndicator.gameObject.SetActive(true);
            }
        }
    }

    private void ThrowPerformed()
    {
        if (IsClient && IsOwner)
        {
            // determine if can throw
            bool canThrow =
            (playerState == PlayerState.Idle || playerState == PlayerState.Moving) &&
            (carryState == PlayerCarryState.CarryingObject || carryState == PlayerCarryState.CarryingPlayer);

            // if the player can throw
            if (canThrow)
            {
                StartCoroutine(TempDisablePickup());
                var playerVelocity = 2f * new Vector3(movement.x, 0, movement.y);
                heldObject.GetComponent<PollutantBehaviour>().OnDropServerRpc(transform.position, transform.forward, playerVelocity, lookVector, throwForce, true);

                // set the carry state to empty
                carryState = PlayerCarryState.Empty;
                UpdatePlayerCarryStateServerRpc(PlayerCarryState.Empty);

                // hide the aim indicator
                aimIndicator.gameObject.SetActive(false);
            }
        }
    }

    private void OnGameStarted()
    {
        canMove = true;
    }

    private void OnGamePaused()
    {
        canMove = false;
    }

    private void OnGameResumed()
    {
        canMove = true;
    }

    private void OnGameStopped()
    {
        // stop moving
        rb.velocity = Vector3.zero;

        canMove = false;
    }

    private void OnDebugEnabled()
    {
        debugCanvasObj.gameObject.SetActive(true);
    }

    private void OnDebugDisabled()
    {
        debugCanvasObj.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        // enable controls
        controls.Gameplay.Enable();

        // setup event listeners
        GameController.DebugEnabled     += OnDebugEnabled;
        GameController.DebugDisabled    += OnDebugDisabled;
        GameController.GameStarted      += OnGameStarted;
        GameController.GamePaused       += OnGamePaused;
        GameController.GameResumed      += OnGameResumed;
        GameController.GameStopped      += OnGameStopped;
    }

    private void OnDisable()
    {
        // disable controls
        controls.Gameplay.Disable();

        // clear event listeners
        GameController.DebugEnabled     -= OnDebugEnabled;
        GameController.DebugDisabled    -= OnDebugDisabled;
        GameController.GameStarted      -= OnGameStarted;
        GameController.GamePaused       -= OnGamePaused;
        GameController.GameResumed      -= OnGameResumed;
        GameController.GameStopped      -= OnGameStopped;
    }

    [ServerRpc(RequireOwnership = false)]
    public void UpdatePlayerCarryStateServerRpc(PlayerCarryState newState)
    {
        networkCarryState.Value = newState;
    }

    public IEnumerator TempDisablePickup()
    {
        justThrew = true;

        yield return new WaitForSeconds(0.50f);

        justThrew = false;
    }
}
