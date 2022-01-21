using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Samples;
using NaughtyAttributes;
using Unity.Netcode.Components;

public class PollutantBehaviour : NetworkBehaviour
{
    public enum PollutantState
    {
        Idle, 
        Airborn
    }

    public Pollutant pollutantObject;
    [ReadOnly] public PollutantState state;

    private TrailRenderer trail;
    private Vector3 throwStartPos;
    private Vector3 throwDestination;

    private Rigidbody rb;
    private SphereCollider sc;
    private NetworkTransform nt;

    private GameObject mesh;

    void Start()
    {
        // setup variables
        trail = gameObject.GetComponent<TrailRenderer>();

        rb = GetComponent<Rigidbody>();
        sc = GetComponent<SphereCollider>();
        nt = GetComponent<NetworkTransform>();

        // RefreshMesh();

        mesh = transform.GetChild(0).gameObject;
    }

    void Update()
    {
        switch (state)
        {
            case PollutantState.Idle:
                // do nothing (for now)
                break;

            case PollutantState.Airborn:
                //transform.position = Vector3.Lerp(throwStartPos, throwDestination, );
                break;
        }
    }

    [Button]
    private void RefreshMesh()
    {
        // check if there is an existing mesh
        Transform oldMesh = transform.Find("Mesh");
        if (oldMesh != null)
        {
            // remove the old mesh
            DestroyImmediate(oldMesh.gameObject);
        }

        // instantiate the new mesh
        GameObject newMesh = Instantiate(pollutantObject.mesh, transform);
        newMesh.name = "Mesh";
    }

    public void Pickup()
    {
        // destroy the gameobject
        Destroy(gameObject);
    }

    private void OnCollisionEnter(Collision collision)
    {
        // switch on other tag
        switch (collision.gameObject.tag)
        {
            // if colliding with the ground
            case "Ground":
                // stop being airborn
                trail.emitting = false;
                state = PollutantState.Idle;
                break;
        }
    }

    public void Throw(Vector3 throwDirection, float throwDistance)
    {
        // enable the trail renderer
        
    }

    [ServerRpc(RequireOwnership = false)]
    public void OnPickupServerRpc()
    {
        transform.localScale = Vector3.zero;

        rb.useGravity = false;
        sc.enabled = false;
    }

    [ServerRpc(RequireOwnership = false)]
    public void OnDropServerRpc(Vector3 playerPos)
    {
        transform.position = new Vector3(playerPos.x, playerPos.y + 2.5f, playerPos.z);
        transform.localScale = new Vector3(1, 1, 1);

        rb.useGravity = true;
        sc.enabled = true;
    }

    [ServerRpc(RequireOwnership = false)]
    public void OnThrowServerRpc(Vector3 playerPos, Vector3 lookVector, float throwForce)
    {
        OnDropServerRpc(playerPos);

        rb.AddForce(lookVector.normalized * throwForce, ForceMode.Impulse);

        trail.emitting = true;
        state = PollutantState.Airborn;
    }
}
