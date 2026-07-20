using UnityEngine;
using System.Collections.Generic;


public class Step1_Observe : ExperimentStep
{


    [Header("需要观察的仪器")]

    public List<HoverInfoDisplay> targetObjects;



    private List<HoverInfoDisplay> observedObjects =
        new List<HoverInfoDisplay>();



    public override void StartStep()
    {

        base.StartStep();


        observedObjects.Clear();


        Debug.Log(
        "步骤一：请观察实验仪器");

    }


    public override void HandleObjectClick(
    HoverInfoDisplay obj)
    {
        ObserveObject(obj);


    }

    // 仪器调用

    public void ObserveObject(
        HoverInfoDisplay obj)
    {


        //不是要求观察的
        if (!targetObjects.Contains(obj))
            return;



        //已经观察过
        if (observedObjects.Contains(obj))
            return;



        observedObjects.Add(obj);



        Debug.Log(
        "已观察:"
        + obj.objectName);



        if (CheckComplete())
        {
            Complete();
        }

    }





    public override bool CheckComplete()
    {

        if (observedObjects.Count
           >= targetObjects.Count)
        {

            return true;

        }


        return false;

    }



    public int GetProgress()
    {

        return observedObjects.Count;

    }


}