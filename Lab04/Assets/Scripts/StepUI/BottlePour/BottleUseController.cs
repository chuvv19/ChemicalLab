using UnityEngine;


public class BottleUseController : MonoBehaviour
{


    public BottleCapController cap;

    public BottlePour pour;


    public void StartPourProcess()
    {


        StartCoroutine(Process());

    }




    System.Collections.IEnumerator Process()
    {


        cap.OpenCap();


        yield return new WaitForSeconds(1);



        pour.Pour();



    }


}