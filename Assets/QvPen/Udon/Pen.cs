using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace QvPen.Udon
{
    public class Pen : UdonSharpBehaviour
    {
        [SerializeField] private GameObject inkPrefab;
        [SerializeField] private Eraser eraser;
        
        [SerializeField] private Transform inkPosition;
        [SerializeField] private Transform spawnTarget;
        [SerializeField] private Transform inkPool;
        
        [SerializeField] private VRC_Pickup pickup;

        [SerializeField] private float followSpeed;
        
        // PenManager
        private PenManager penManager;
        private bool isUser;

        // Ink
        private GameObject inkInstance;
        private int inkCount;

        // Double click
        private bool useDoubleClick;
        private float prevClickTime;
        private const float ClickInterval = 0.2f;

        // State
        private const int StatePenIdle = 0;
        private const int StatePenUsing = 1;
        private const int StateEraserIdle = 2;
        private const int StateEraserUsing = 3;
        private int currentState = StatePenIdle;

        public void Init(PenManager manager)
        {
            penManager = manager;
            eraser.SetActive(false);
            inkPrefab.gameObject.SetActive(false);

            pickup.InteractionText = nameof(Pen);
            pickup.UseText = "Draw";
        }
        
        private void LateUpdate()
        {
            if (isUser)
            {
                spawnTarget.position = Vector3.Lerp(spawnTarget.position, inkPosition.position, Time.deltaTime * followSpeed);
                spawnTarget.rotation = Quaternion.Lerp(spawnTarget.rotation, inkPosition.rotation, Time.deltaTime * followSpeed);
            }
            else
            {
                spawnTarget.position = inkPosition.position;
                spawnTarget.rotation = inkPosition.rotation;
            }
        }
        
        #region Events
        
        public override void OnPickup()
        {
            isUser = true;
            penManager.StartUsing();
            
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ChangeStateToPenIdle));
        }
        
        public override void OnDrop()
        {
            isUser = false;
            penManager.EndUsing();
            
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ChangeStateToPenIdle));
        }
        
        public override void OnPickupUseDown()
        {
            if (useDoubleClick && Time.time - prevClickTime < ClickInterval)
            {
                prevClickTime = 0f;
                switch (currentState)
                {
                    case StatePenIdle:
                        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(DestroyJustBeforeInk));
                        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ChangeStateToEraseIdle));
                        break;
                    case StateEraserIdle:
                        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ChangeStateToPenIdle));
                        break;
                    default:
                        Debug.LogError($"Unexpected state : {currentState} at {nameof(OnPickupUseDown)} Double Clicked");
                        break;
                }
            }
            else
            {
                prevClickTime = Time.time;
                switch (currentState)
                {
                    case StatePenIdle:
                        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ChangeStateToPenUsing));
                        break;
                    case StateEraserIdle:
                        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ChangeStateToEraseUsing));
                        break;
                    default:
                        Debug.LogError($"Unexpected state : {currentState} at {nameof(OnPickupUseDown)}");
                        break;
                }
            }
        }

        public override void OnPickupUseUp()
        {
            switch (currentState)
            {
                case StatePenUsing:
                    SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ChangeStateToPenIdle));
                    break;
                case StateEraserUsing:
                    SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ChangeStateToEraseIdle));
                    break;
                default:
                    Debug.LogError($"Unexpected state : {currentState} at {nameof(OnPickupUseUp)}");
                    break;
            }
        }

        public void SetUseDoubleClick(bool value)
        {
            useDoubleClick = value;
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ChangeStateToPenIdle));
        }
        
        #endregion

        public void DestroyJustBeforeInk()
        {
            var childCount = inkPool.childCount;
            if (childCount > 0)
            {
                Destroy(inkPool.GetChild(childCount - 1).gameObject);
            }
        }

        #region ChangeState

        public void ChangeStateToPenIdle()
        {
            switch (currentState)
            {
                case StatePenUsing:
                    FinishDrawing();
                    break;
                case StateEraserIdle:
                    ChangeToPen();
                    break;
                case StateEraserUsing:
                    FinishErasing();
                    ChangeToPen();
                    break;
            }
            currentState = StatePenIdle;
        }
        
        public void ChangeStateToPenUsing()
        {
            switch (currentState)
            {
                case StatePenIdle:
                    StartDrawing();
                    break;
                case StateEraserIdle:
                    ChangeToPen();
                    StartDrawing();
                    break;
                case StateEraserUsing:
                    FinishErasing();
                    ChangeToPen();
                    StartDrawing();
                    break;
            }
            currentState = StatePenUsing;
        }

        public void ChangeStateToEraseIdle()
        {
            switch (currentState)
            {
                case StatePenIdle:
                    ChangeToEraser();
                    break;
                case StatePenUsing:
                    FinishDrawing();
                    ChangeToEraser();
                    break;
                case StateEraserUsing:
                    FinishErasing();
                    break;
            }
            currentState = StateEraserIdle;
        }

        public void ChangeStateToEraseUsing()
        {
            switch (currentState)
            {
                case StatePenIdle:
                    ChangeToEraser();
                    StartErasing();
                    break;
                case StatePenUsing:
                    FinishDrawing();
                    ChangeToEraser();
                    StartErasing();
                    break;
                case StateEraserIdle:
                    StartErasing();
                    break;
            }
            currentState = StateEraserUsing;
        }
        
        #endregion

        public void Respawn()
        {
            if (pickup) pickup.Drop();
            if (Networking.LocalPlayer.IsOwner(gameObject))
            {
                transform.localPosition = Vector3.zero;
                transform.localRotation = Quaternion.identity;
            }
        }
        
        public void Clear()
        {
            for (var i = 0; i < inkPool.childCount; i++)
            {
                Destroy(inkPool.GetChild(i).gameObject);
            }
        }

        private void StartDrawing()
        {
            inkInstance = VRCInstantiate(inkPrefab.gameObject);
            inkInstance.name = $"{inkPrefab.name}{inkCount++:000000}";

            inkInstance.transform.SetParent(spawnTarget);
            inkInstance.transform.localPosition = Vector3.zero;
            inkInstance.transform.localRotation = Quaternion.identity;
            inkInstance.SetActive(true);
            inkInstance.GetComponent<TrailRenderer>().enabled = true;
        }

        private void FinishDrawing()
        {
            if (inkInstance != null)
            {
                inkInstance.transform.SetParent(inkPool);
                CreateInkMeshCollider();
            }

            inkInstance = null;
        }

        private void StartErasing()
        {
            eraser.StartErasing();
        }

        private void FinishErasing()
        {
            eraser.FinishErasing();
        }

        private void ChangeToPen()
        {
            eraser.FinishErasing();
            eraser.SetActive(false);
        }
        
        private void ChangeToEraser()
        {
            eraser.SetActive(true);
        }

        private void CreateInkMeshCollider()
        {
            var trailRenderer = inkInstance.GetComponent<TrailRenderer>();
            var meshFilter = inkInstance.GetComponent<MeshFilter>();
            var meshCollider = inkInstance.GetComponent<MeshCollider>();
            
            var positionCount = trailRenderer.positionCount;
            if (positionCount < 2)
            {
                positionCount = 2;
            }

            var verticesParPoint = 2;
            var trianglesCountParPoint = 6;
            var positions = new Vector3[positionCount];
            var vertices = new Vector3[positionCount * verticesParPoint];
            var triangles = new int[(positionCount - 1) * trianglesCountParPoint];

            var colliderWidth = 0.005f;
            var offsetXP = new Vector3(colliderWidth, 0, 0);
            var offsetXN = new Vector3(-colliderWidth, 0, 0);

            trailRenderer.GetPositions(positions);
            if (positionCount == 2)
            {
                positions[0] =inkInstance.transform.position;
                positions[1] =inkInstance.transform.position + Vector3.down * colliderWidth;
            }

            // Create vertices
            for (var i = 0; i < positionCount; i++)
            {
                var position = inkInstance.transform.InverseTransformPoint(positions[i]);
                vertices[i * verticesParPoint + 0] = position + offsetXP;
                vertices[i * verticesParPoint + 1] = position + offsetXN;
            }

            // Create triangles
            for (var i = 0; i < positionCount - 1; i++)
            {
                triangles[i * trianglesCountParPoint + 0] = i * verticesParPoint + 0;
                triangles[i * trianglesCountParPoint + 1] = i * verticesParPoint + 1;
                triangles[i * trianglesCountParPoint + 2] = i * verticesParPoint + 2;
                triangles[i * trianglesCountParPoint + 3] = i * verticesParPoint + 3;
                triangles[i * trianglesCountParPoint + 4] = i * verticesParPoint + 2;
                triangles[i * trianglesCountParPoint + 5] = i * verticesParPoint + 1;
            }

            // Instantiate a mesh
            var mesh = meshFilter.mesh;

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();

            meshFilter.mesh = mesh;
            meshCollider.sharedMesh = mesh;
        }
    }
}