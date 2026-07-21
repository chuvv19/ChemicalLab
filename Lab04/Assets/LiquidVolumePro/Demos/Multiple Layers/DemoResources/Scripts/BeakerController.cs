using UnityEngine;

namespace LiquidVolumeFX
{
	public class BeakerController : MonoBehaviour
	{

		[Range (0, 2f)]
		public float rotationSpeed = 1f;



		LiquidVolume lv;

		void OnEnable() {
			InputProxy.SetupEventSystem();
		}

		void Start ()
		{
			lv = GetComponentInChildren<LiquidVolume> ();
		}

		void Update ()
		{
			if (InputProxy.GetKey (KeyCode.W)) {
				transform.Rotate (0, 0, rotationSpeed);
			} else if (InputProxy.GetKey (KeyCode.S)) {
				transform.Rotate (0, 0, -rotationSpeed);
			}

			if (InputProxy.GetKey (KeyCode.Q)) {
				lv.liquidLayers [0].amount += 0.01f;
				lv.UpdateLayers (true);
			}
			if (InputProxy.GetKey (KeyCode.A)) {
				lv.liquidLayers [0].amount -= 0.01f;
				lv.UpdateLayers (true);
			}

			if (InputProxy.GetKeyDown (KeyCode.R)) {
				SetRandomProperties ();
			}

			if (InputProxy.GetKeyDown(KeyCode.F)) {
				FourLayersExample();
			}

		}


		void SetRandomProperties() {

            int layerCount = lv.liquidLayers.Length;
            if (layerCount == 0)
                return;

            float fillLevel = 0;
            for (int k = 0; k < layerCount; k++) {
                float layerAmount = (1.0f - fillLevel) * Random.value;
                fillLevel += layerAmount;
                lv.liquidLayers[k].amount = layerAmount;
                lv.liquidLayers[k].bubblesOpacity = Random.value;
                lv.liquidLayers[k].color = new Color(Random.value, Random.value, Random.value, Random.value);
                lv.liquidLayers[k].murkColor = new Color(Random.value, Random.value, Random.value, Random.value);
                lv.liquidLayers[k].murkiness = Random.value;
            }
            // Immediately update layers
            lv.UpdateLayers(true);

        }


        void FourLayersExample() {
			LiquidVolume.LiquidLayer[] layers = new LiquidVolume.LiquidLayer[4];
			layers[0].amount = 0.25f;
			layers[0].density = 1f;
			layers[1].amount = 0;
			layers[1].density = 1f;
			layers[2].amount = 0;
			layers[2].density = 1f;
			layers[3].amount = 0;
			layers[3].density = 1f;
			layers[0].color = new Color(1, 0, 0);
			layers[1].color = new Color(0, 1, 0);
			layers[2].color = new Color(0, 0, 1);
			layers[3].color = new Color(0.5f, 0.5f, 0.5f);
			lv.liquidLayers = layers;
			lv.UpdateLayers();
		}

	}

}