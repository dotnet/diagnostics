@Library('dotnet-ci') _
import jobs.generation.Utilities

// Possible OS's
//
// 'Windows_NT'
// 'Ubuntu'
// 'Ubuntu16.04'
// 'Ubuntu16.10'
// 'Debian8.4'
// 'RHEL7.2'
// 'Fedora24'
// 'CentOS7.1'
// 'OSX10.12'

// Possible Architechures
//
// 'arm', 
// 'arm64'
// 'x86'
// 'x64'

def buildConfigurations = [
    ['OS':'Windows_NT', 'Architechure':'x64', 'Configuration':'Release'],
    ['OS':'Ubuntu16.04', 'Architechure':'x64', 'Configuration':'Release'],
    ['OS':'CentOS7.1', 'Architechure':'x64', 'Configuration':'Release'],
]

def testConfigurations = [
    ['OS':'Ubuntu16.04', 'Architechure':'x64', 'Configuration':'Release'],
]

buildConfigurations.each { config ->

    simpleNode(config.OS, 'latest') {

        stage ('Checkout Source') {
	    checkout scm
        }
	
        stage ('Build/Test') {

            if (os == "Windows_NT") {
                bat ".\\eng\\common\\CIBuild.cmd -configuration ${config.Configuration} -prepareMachine"
            } else {
                sh "./eng/cibuild.sh --configuration ${config.Configuration} --architechure ${config.Architechure} --prepareMachine"
            }
        }

        stage ('Archive artifacts') {
            def resultFilePattern = "**/artifacts/${config.Configuration}/TestResults/*.xml"
            Utilities.addXUnitDotNETResults(job, resultFilePattern, skipIfNoTestFiles: false)

	    def filesToArchive = "**/artifacts/${config.Configuration}/**"
            archiveArtifacts allowEmptyArchive: true, artifacts: filesToArchive
        }
    }
}
