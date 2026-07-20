using UnityEngine;


public abstract class ExperimentStep : MonoBehaviour
{


    public string stepName;


    protected bool completed = false;



    public virtual void StartStep()
    {

        completed = false;


        Debug.Log(
            "开始步骤：" + stepName
        );

    }

    public virtual void HandleObjectClick(
    HoverInfoDisplay obj)
    {


    }

    // 给子步骤重写检查条件

    public virtual bool CheckComplete()
    {

        return completed;

    }



    public void Complete()
    {

        if (completed)
            return;


        completed = true;



        Debug.Log(
            "完成步骤：" + stepName
        );


        if (ExperimentManager.Instance != null)
        {

            ExperimentManager.Instance.NextStep();

        }

    }

    public bool IsCompleted()
    {

        return completed;

    }
}