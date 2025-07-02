using Reqnroll;
using System;
using Xunit;

namespace uptime.codechallenge.Specs.StepDefinitions
{
    [Binding]
    public class ProcessFuelSensorDataStepDefinitions : ICollectionFixture<ProcessFuelSensorFixture>
    {
        private readonly ProcessFuelSensorFixture _fixture;
        public ProcessFuelSensorDataStepDefinitions(ProcessFuelSensorFixture fixture )
        {
            _fixture = fixture;


            Task.Run(async () => {

                try
                {
                    await _fixture.InitializeAsync();

                }
                catch (Exception)
                {

                    throw;
                }

            });

        }
        [Given("the example excel file is readed and dispatched to kafka topic")]
        public void GivenTheExampleExcelFileIsReadedAndDispatchedToKafkaTopic(DataTable dataTable)
        {
      
        }

        [When("fuel sensor interpeter is calibrated, noise is cleaned and data is stored")]
        public void WhenFuelSensorInterpeterIsCalibratedNoiseIsCleanedAndDataIsStored()
        {
            throw new PendingStepException();
        }

        [Then("From date {string} TO {string} is ready for query")]
        public void ThenFromDateTOIsReadyForQuery(string p0, string p1)
        {
            throw new PendingStepException();
        }
    }
}
