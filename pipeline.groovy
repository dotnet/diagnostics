@Library('dotnet-ci') _
import jobs.generation.Utilities

// Accepts parameters
// OS - Windows_NT, Ubuntu, Ubuntu16.04, Ubuntu16.10, Debian8.4, CentOS7.1, RHEL7.2, Fedora24
// Architechure - x64, x86, arm, arm64
// Configuration - Debug or Release

def os = params.OS
def architechure = params.Architechure
def configuration = params.Configuration

// build and test
simpleNode(os, 'latest') {

    stage ('Checkout Source') {
	checkout scm
    }
	
    stage ('Build/Test') {

        if (os == "Windows_NT") {
            bat ".\\eng\\common\\CIBuild.cmd -configuration ${configuration} -prepareMachine"
        } else {
            sh "./eng/cibuild.sh --configuration ${configuration} --architechure ${architechure} --prepareMachine"
        }
    }

    stage ('Archive artifacts') {
        def resultFilePattern = "**/artifacts/${configuration}/TestResults/*.xml"
        Utilities.addXUnitDotNETResults(job, resultFilePattern, skipIfNoTestFiles: false)

	def filesToArchive = "**/artifacts/${configuration}/**"
        archiveArtifacts allowEmptyArchive: true, artifacts: filesToArchive
    }
}
