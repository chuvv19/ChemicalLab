using UnityEngine;
using System.Collections.Generic;


public class Step2_1_AddStyrene : ExperimentStep
{
    public bool bottleSelected;

    public bool liquidAdded;

    public HoverInfoDisplay styreneBottle;

    public override void StartStep()
    {

        base.StartStep();


        Debug.Log(
        "请向100ml分液漏斗加入50ml苯乙烯");

    }

    public override void HandleObjectClick(
    HoverInfoDisplay obj)
    {

        if (obj.objectName == "苯乙烯")
        {

            obj.GetComponent<BottleUseController>()
            .StartPourProcess();

        }


    }

    public void SelectBottle()
    {

        bottleSelected = true;

        Check();

    }




    public void AddLiquid()
    {

        if (!bottleSelected)
        {

            Debug.Log(
            "请先选择苯乙烯瓶");

            return;

        }


        liquidAdded = true;


        Check();

    }



    void Check()
    {

        if (bottleSelected &&
           liquidAdded)
        {

            Complete();

        }

    }

    public void PourFinished()
    {

        Step2_1_AddStyrene step =
        ExperimentManager.Instance.CurrentStep()
        as Step2_1_AddStyrene;


        if (step)
        {
            step.AddLiquid();
        }

    }

}